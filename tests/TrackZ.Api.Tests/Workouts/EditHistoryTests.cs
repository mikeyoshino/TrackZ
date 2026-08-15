using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Workouts;

public sealed class EditHistoryTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private EditHistoryApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_edit_history_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_edit_history_tests_only")
            .Build();
        try
        {
            await _container.StartAsync();
        }
        catch (DockerUnavailableException exception)
        {
            await _container.DisposeAsync();
            throw SkipException.ForSkip(
                $"Docker is unavailable; edit-history API tests require Docker. {exception.Message}");
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", "trackz-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "trackz-mobile");
        Environment.SetEnvironmentVariable(
            "Jwt__SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "14");
        _factory = new EditHistoryApiFactory(_container.GetConnectionString());
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", null);
        Environment.SetEnvironmentVariable("Jwt__Issuer", null);
        Environment.SetEnvironmentVariable("Jwt__Audience", null);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", null);
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", null);
    }

    [Fact]
    public async Task Complete_edit_set_delete_set_and_delete_workout_are_exactly_once_and_recalculate_history()
    {
        var owner = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"History Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = new SyncIds(Guid.NewGuid(), Guid.NewGuid());
        var startedAt = Utc(8);
        await AssertAppliedAsync(owner.Token, Start(ids, exercise.Id, startedAt), 1);
        var firstSetId = Guid.NewGuid();
        var secondSetId = Guid.NewGuid();
        await AssertAppliedAsync(owner.Token, SaveSet(
            ids, Guid.NewGuid(), firstSetId, 1, 0, "70", 10, startedAt.AddMinutes(1)), 2);
        await AssertAppliedAsync(owner.Token, SaveSet(
            ids, Guid.NewGuid(), secondSetId, 2, 1, "72.5", 8, startedAt.AddMinutes(2)), 3);
        var complete = Operation(Guid.NewGuid(), "CompleteWorkout", 3, new
        {
            workoutId = ids.WorkoutId,
            completedAt = startedAt.AddMinutes(10)
        });
        await AssertAppliedAsync(owner.Token, complete, 4);

        var completedHistory = await ReadJsonAsync(owner.Token, "/api/v1/workouts");
        var completedWorkout = Assert.Single(
            completedHistory.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal([10, 8], completedWorkout.GetProperty("exercises")[0]
            .GetProperty("sets").EnumerateArray().Select(set => set.GetProperty("reps").GetInt32()));

        var editOperationId = Guid.NewGuid();
        var edit = Operation(editOperationId, "EditSet", 4, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId = firstSetId,
            weightKg = "75.125",
            assistedKg = (string?)null,
            reps = 6,
            updatedAt = startedAt.AddMinutes(11)
        });
        var firstEdit = await PushDocumentAsync(owner.Token, edit);
        var editReplay = await PushDocumentAsync(owner.Token, edit);
        AssertResult(firstEdit, "Applied", 5, null);
        Assert.Equal(Result(firstEdit).GetRawText(), Result(editReplay).GetRawText());

        var alteredReplay = await PushDocumentAsync(owner.Token,
            Operation(editOperationId, "EditSet", 4, new
            {
                workoutId = ids.WorkoutId,
                workoutExerciseId = ids.WorkoutExerciseId,
                setId = firstSetId,
                weightKg = "80",
                assistedKg = (string?)null,
                reps = 5,
                updatedAt = startedAt.AddMinutes(12)
            }));
        AssertResult(alteredReplay, "Rejected", null, 10009);

        var edited = await ReadJsonAsync(owner.Token, $"/api/v1/workouts/{ids.WorkoutId:D}");
        var editedSets = edited.RootElement.GetProperty("exercises")[0]
            .GetProperty("sets").EnumerateArray().ToArray();
        Assert.Equal("75.125", editedSets[0].GetProperty("weightKg").GetDecimal().ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(6, editedSets[0].GetProperty("reps").GetInt32());

        var deleteSet = Operation(Guid.NewGuid(), "DeleteSet", 5, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId = firstSetId,
            deletedAt = startedAt.AddMinutes(12)
        });
        await AssertAppliedAsync(owner.Token, deleteSet, 6);
        var afterSetDelete = await ReadJsonAsync(owner.Token, $"/api/v1/workouts/{ids.WorkoutId:D}");
        var remainingSet = Assert.Single(afterSetDelete.RootElement.GetProperty("exercises")[0]
            .GetProperty("sets").EnumerateArray());
        Assert.Equal(secondSetId, remainingSet.GetProperty("id").GetGuid());
        Assert.Equal(0, remainingSet.GetProperty("order").GetInt32());

        var deleteWorkout = Operation(Guid.NewGuid(), "DeleteWorkout", 6, new
        {
            workoutId = ids.WorkoutId,
            deletedAt = startedAt.AddMinutes(13)
        });
        var firstDelete = await PushDocumentAsync(owner.Token, deleteWorkout);
        var deleteReplay = await PushDocumentAsync(owner.Token, deleteWorkout);
        AssertResult(firstDelete, "Applied", 7, null);
        Assert.Equal(Result(firstDelete).GetRawText(), Result(deleteReplay).GetRawText());
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(owner.Token, $"/api/v1/workouts/{ids.WorkoutId:D}")).StatusCode);
        var emptyHistory = await ReadJsonAsync(owner.Token, "/api/v1/workouts");
        Assert.Empty(emptyHistory.RootElement.GetProperty("items").EnumerateArray());

        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workout = await database.WorkoutSessions.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == ids.WorkoutId);
        var sets = await database.SetEntries.IgnoreQueryFilters()
            .Where(item => item.WorkoutExerciseId == ids.WorkoutExerciseId)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.NotNull(workout.DeletedAt);
        Assert.Single(sets, set => set.DeletedAt is not null);
        Assert.Single(sets, set => set.DeletedAt is null);
        Assert.Equal(7, await database.SyncChanges.CountAsync(change =>
            change.OwnerId == owner.UserId && change.EntityId == ids.WorkoutId));
    }

    [Fact]
    public async Task Historical_mutations_are_owner_scoped_validate_shape_and_report_stale_base_conflicts()
    {
        var owner = await AuthenticateAsync();
        var other = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"History Owner Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = new SyncIds(Guid.NewGuid(), Guid.NewGuid());
        var setId = Guid.NewGuid();
        var startedAt = Utc(10);
        await AssertAppliedAsync(owner.Token, Start(ids, exercise.Id, startedAt), 1);
        await AssertAppliedAsync(owner.Token, SaveSet(
            ids, Guid.NewGuid(), setId, 1, 0, "70", 10, startedAt.AddMinutes(1)), 2);
        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "CompleteWorkout", 2, new
        {
            workoutId = ids.WorkoutId,
            completedAt = startedAt.AddMinutes(10)
        }), 3);

        var foreign = await PushDocumentAsync(other.Token, Operation(Guid.NewGuid(), "EditSet", 3, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            weightKg = "90",
            assistedKg = (string?)null,
            reps = 1,
            updatedAt = startedAt.AddMinutes(11)
        }));
        AssertResult(foreign, "Rejected", null, 10009);

        var stale = await PushDocumentAsync(owner.Token, Operation(Guid.NewGuid(), "DeleteSet", 2, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            deletedAt = startedAt.AddMinutes(11)
        }));
        AssertResult(stale, "Conflict", 3, 60001);

        foreach (var malformed in new[]
                 {
                     Operation(Guid.NewGuid(), "CompleteWorkout", 3, new { workoutId = ids.WorkoutId }),
                     Operation(Guid.NewGuid(), "EditSet", 3, new
                     {
                         workoutId = ids.WorkoutId, workoutExerciseId = ids.WorkoutExerciseId,
                         setId, weightKg = "70", reps = 8, updatedAt = startedAt.AddMinutes(11)
                     }),
                     Operation(Guid.NewGuid(), "DeleteSet", 3, new
                     {
                         workoutId = ids.WorkoutId, workoutExerciseId = ids.WorkoutExerciseId,
                         deletedAt = startedAt.AddMinutes(11)
                     }),
                     Operation(Guid.NewGuid(), "DeleteWorkout", 3, new { workoutId = ids.WorkoutId })
                 })
        {
            AssertResult(await PushDocumentAsync(owner.Token, malformed), "Rejected", null, 10009);
        }

        var unchanged = await ReadJsonAsync(owner.Token, $"/api/v1/workouts/{ids.WorkoutId:D}");
        var unchangedSet = Assert.Single(unchanged.RootElement.GetProperty("exercises")[0]
            .GetProperty("sets").EnumerateArray());
        Assert.Equal(70m, unchangedSet.GetProperty("weightKg").GetDecimal());
    }

    [Fact]
    public async Task History_mutation_sync_route_is_authorization_first()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/sync/push", new
        {
            operations = new[]
            {
                Operation(Guid.NewGuid(), "DeleteWorkout", 1, new
                {
                    workoutId = Guid.NewGuid(), deletedAt = Utc(12)
                })
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ProcessedClientOperations.ToListAsync());
    }

    [Fact]
    public async Task Recreated_mobile_sqlite_syncs_completed_workout_exactly_once_to_real_postgresql()
    {
        var owner = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Offline E2E Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var sqlitePath = Path.Combine(
            Path.GetTempPath(), $"trackz-real-sync-{Guid.NewGuid():N}.db");
        try
        {
            var clock = new TestClock(Utc(14));
            var boundary = new AccountSessionBoundary();
            var database = new TrackZLocalDatabase(sqlitePath);
            var active = new ActiveWorkoutCoordinator(
                new LocalWorkoutRepository(database), boundary, clock);
            var started = await active.StartAsync(
                [new WorkoutExerciseSelection(exercise.Id, TrackingMode.Weighted)]);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var first = await active.SaveSetAsync(exercise.Id, new LocalSet(70m, null, 10));
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var second = await active.SaveSetAsync(exercise.Id, new LocalSet(72.5m, null, 8));

            // Recreate all local persistence/coordinator objects before completion and reconnect.
            database = new TrackZLocalDatabase(sqlitePath);
            active = new ActiveWorkoutCoordinator(
                new LocalWorkoutRepository(database), boundary, clock);
            Assert.Equal(started.Id, (await active.RestoreActiveAsync())!.Id);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var completed = await active.FinishAsync();
            using var mobileHttp = _factory!.CreateClient();
            mobileHttp.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", owner.Token);
            var sync = new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath),
                new TrackZSyncApiClient(mobileHttp),
                boundary,
                clock);

            Assert.Equal(SyncRunStatus.Completed, await sync.RunOnceAsync());
            Assert.Equal(SyncRunStatus.Completed, await sync.RunOnceAsync());

            var local = Assert.Single(await new LocalWorkoutRepository(
                new TrackZLocalDatabase(sqlitePath)).GetHistoryAsync());
            Assert.Equal(completed.Id, local.Id);
            Assert.Equal([first.Id, second.Id], Assert.Single(local.Exercises).Sets
                .OrderBy(set => set.Order).Select(set => set.Id));
            Assert.Equal([10, 8], Assert.Single(local.Exercises).Sets
                .OrderBy(set => set.Order).Select(set => set.Reps));

            var history = await ReadJsonAsync(owner.Token, "/api/v1/workouts");
            var serverWorkout = Assert.Single(
                history.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(completed.Id, serverWorkout.GetProperty("id").GetGuid());
            Assert.Equal([first.Id, second.Id], serverWorkout.GetProperty("exercises")[0]
                .GetProperty("sets").EnumerateArray().Select(set => set.GetProperty("id").GetGuid()));
            Assert.Equal([10, 8], serverWorkout.GetProperty("exercises")[0]
                .GetProperty("sets").EnumerateArray().Select(set => set.GetProperty("reps").GetInt32()));

            await using var scope = _factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var completedExerciseId = Assert.Single(completed.Exercises).Id;
            Assert.Single(await store.WorkoutSessions.Where(item => item.Id == completed.Id).ToListAsync());
            Assert.Equal(2, await store.SetEntries.CountAsync(item =>
                item.WorkoutExerciseId == completedExerciseId));
            Assert.Equal(4, await store.ProcessedClientOperations.CountAsync(item =>
                item.UserId == owner.UserId));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = sqlitePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static object Operation(Guid operationId, string action, long baseVersion, object payload) => new
    {
        operationId,
        entityType = "Workout",
        action,
        payload,
        baseVersion
    };

    private static object Start(SyncIds ids, Guid exerciseId, DateTimeOffset startedAt) =>
        Operation(Guid.NewGuid(), "StartWorkout", 0, new
        {
            workoutId = ids.WorkoutId,
            startedAt,
            exercises = new[]
            {
                new
                {
                    workoutExerciseId = ids.WorkoutExerciseId,
                    exerciseDefinitionId = exerciseId,
                    trackingMode = 1,
                    order = 0
                }
            }
        });

    private static object SaveSet(
        SyncIds ids,
        Guid operationId,
        Guid setId,
        long baseVersion,
        int order,
        string weightKg,
        int reps,
        DateTimeOffset completedAt) => Operation(operationId, "SaveSet", baseVersion, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            order,
            weightKg,
            assistedKg = (string?)null,
            reps,
            completedAt
        });

    private async Task AssertAppliedAsync(string token, object operation, long serverVersion) =>
        AssertResult(await PushDocumentAsync(token, operation), "Applied", serverVersion, null);

    private async Task<JsonDocument> PushDocumentAsync(string token, object operation)
    {
        var response = await SendAsync(token, "/api/v1/sync/push", new { operations = new[] { operation } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
    }

    private async Task<JsonDocument> ReadJsonAsync(string token, string path)
    {
        var response = await SendAsync(token, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(
            body is null ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private static JsonElement Result(JsonDocument response) =>
        response.RootElement.GetProperty("results")[0];

    private static void AssertResult(
        JsonDocument response,
        string status,
        long? serverVersion,
        int? errorCode)
    {
        var result = Result(response);
        Assert.Equal(status, result.GetProperty("status").GetString());
        Assert.Equal(serverVersion, result.GetProperty("serverVersion").ValueKind == JsonValueKind.Null
            ? null : result.GetProperty("serverVersion").GetInt64());
        Assert.Equal(errorCode, result.GetProperty("errorCode").ValueKind == JsonValueKind.Null
            ? null : result.GetProperty("errorCode").GetInt32());
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync()
    {
        var email = $"edit-history-{Guid.NewGuid():N}@example.com";
        var registration = await (await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "ValidPassword!42"
        })).Content.ReadFromJsonAsync<RegistrationResponse>();
        var login = await (await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "ValidPassword!42",
            deviceName = "edit-history-tests"
        })).Content.ReadFromJsonAsync<TokenResponse>();
        return (registration!.UserId, login!.AccessToken);
    }

    private async Task SeedAsync(ExerciseDefinition exercise)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Exercises.Add(exercise);
        await database.SaveChangesAsync();
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 16, hour, 0, 0, TimeSpan.Zero);

    private sealed record SyncIds(Guid WorkoutId, Guid WorkoutExerciseId);
    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class EditHistoryApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:TrackZ"] = connectionString,
                    ["Jwt:Issuer"] = "trackz-api",
                    ["Jwt:Audience"] = "trackz-mobile",
                    ["Jwt:SigningKey"] = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "14"
                }))
            .ConfigureServices(services => services.ReplaceStagingLifecycleWithNoOpForTests());
    }
}
