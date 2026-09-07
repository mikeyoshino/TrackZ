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
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Sync;
using TrackZ.Domain.Workouts;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Sync;

public sealed class SyncPullTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private SyncPullApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_sync_pull_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_sync_pull_tests_only")
            .Build();
        try { await _container.StartAsync(); }
        catch (DockerUnavailableException exception)
        {
            await _container.DisposeAsync();
            throw SkipException.ForSkip($"Docker is unavailable; sync pull tests require Docker. {exception.Message}");
        }
        SetEnvironment(_container.GetConnectionString());
        _factory = new SyncPullApiFactory(_container.GetConnectionString());
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
        SetEnvironment(null);
    }

    [Fact]
    public async Task Pull_authenticates_before_reading_or_validating_the_cursor()
    {
        var response = await _client.GetAsync("/api/v1/sync/pull?cursor=not-a-valid-cursor");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Applied_pushes_emit_exactly_once_changes_and_cursor_replay_is_stable()
    {
        var account = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Pull Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var workoutId = Guid.NewGuid();
        var workoutExerciseId = Guid.NewGuid();
        var operation = new
        {
            operationId = Guid.NewGuid(), entityType = "Workout", action = "StartWorkout",
            baseVersion = 0,
            payload = new
            {
                workoutId, startedAt = Utc(8),
                exercises = new[] { new { workoutExerciseId, exerciseDefinitionId = exercise.Id, trackingMode = 1, order = 0 } }
            }
        };
        await PushAsync(account.Token, operation);
        await PushAsync(account.Token, operation);
        var setId = Guid.NewGuid();
        var setOperation = new
        {
            operationId = Guid.NewGuid(), entityType = "Workout", action = "SaveSet",
            baseVersion = 1,
            payload = new
            {
                workoutId, workoutExerciseId, setId, order = 0,
                weightKg = "70", assistedKg = (string?)null, reps = 8,
                completedAt = Utc(8).AddMinutes(1)
            }
        };
        await PushAsync(account.Token, setOperation);
        await PushAsync(account.Token, setOperation);
        var effortOperation = new
        {
            operationId = Guid.NewGuid(),
            entityType = "Workout",
            action = "RecordSetEffort",
            baseVersion = 2,
            payload = new
            {
                workoutId,
                workoutExerciseId,
                setId,
                effort = (int)SetEffortRating.Productive,
                recordedAt = Utc(8).AddMinutes(2)
            }
        };
        await PushAsync(account.Token, effortOperation);
        await PushAsync(account.Token, effortOperation);

        var first = await PullAsync(account.Token, null, 1);
        var replay = await PullAsync(account.Token, null, 1);
        var second = await PullAsync(account.Token, first.NextCursor, 1);
        var third = await PullAsync(account.Token, second.NextCursor, 1);

        var change = Assert.Single(first.Changes);
        Assert.Equal(workoutId, change.EntityId);
        Assert.Equal(1, change.ServerVersion);
        Assert.Empty(change.Workout.Exercises[0].Sets);
        Assert.True(first.HasMore);
        var savedSet = Assert.Single(Assert.Single(second.Changes).Workout.Exercises[0].Sets);
        var ratedSet = Assert.Single(Assert.Single(third.Changes).Workout.Exercises[0].Sets);
        Assert.Null(savedSet.Effort);
        Assert.Equal(SetEffortRating.Productive, ratedSet.Effort);
        Assert.Equal("70", savedSet.WeightKg);
        Assert.Equal("70", ratedSet.WeightKg);
        Assert.Equal(savedSet.WeightKg, ratedSet.WeightKg);
        Assert.Equal(savedSet.Reps, ratedSet.Reps);
        Assert.Equal(2, second.Changes[0].ServerVersion);
        Assert.Equal(3, third.Changes[0].ServerVersion);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.NotNull(first.NextCursor);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await database.SyncChanges.CountAsync(item => item.OperationId == operation.operationId));
        Assert.Equal(1, await database.SyncChanges.CountAsync(item => item.OperationId == setOperation.operationId));
        Assert.Equal(1, await database.SyncChanges.CountAsync(
            item => item.OperationId == effortOperation.operationId));
    }

    [Fact]
    public async Task Pull_is_owner_scoped_and_rejects_tampered_or_cross_owner_cursors()
    {
        var owner = await AuthenticateAsync();
        var other = await AuthenticateAsync();
        await AddChangeAsync(owner.UserId, Guid.NewGuid(), Graph(Guid.NewGuid(), deleted: false), Utc(9));
        await AddChangeAsync(other.UserId, Guid.NewGuid(), Graph(Guid.NewGuid(), deleted: false), Utc(9));

        var ownerPage = await PullAsync(owner.Token, null, 10);

        Assert.Single(ownerPage.Changes);
        Assert.Equal(owner.UserId, await OwnerOfAsync(ownerPage.Changes[0].EntityId));
        var tampered = ownerPage.NextCursor![..^1] + (ownerPage.NextCursor[^1] == 'A' ? 'B' : 'A');
        Assert.Equal(HttpStatusCode.BadRequest, (await PullResponseAsync(owner.Token, tampered, 10)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PullResponseAsync(other.Token, ownerPage.NextCursor, 10)).StatusCode);
    }

    [Fact]
    public async Task Equal_timestamp_pages_are_sequence_ordered_and_tombstones_are_replayable()
    {
        var account = await AuthenticateAsync();
        var changedAt = Utc(10);
        var firstWorkout = Graph(Guid.NewGuid(), deleted: false);
        var deletedWorkout = Graph(Guid.NewGuid(), deleted: true);
        await AddChangeAsync(account.UserId, Guid.NewGuid(), firstWorkout, changedAt);
        await AddChangeAsync(account.UserId, Guid.NewGuid(), deletedWorkout, changedAt);

        var first = await PullAsync(account.Token, null, 1);
        var second = await PullAsync(account.Token, first.NextCursor, 1);
        var replay = await PullAsync(account.Token, first.NextCursor, 1);

        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
        Assert.True(first.Changes[0].Sequence < second.Changes[0].Sequence);
        Assert.True(second.Changes[0].IsDeleted);
        Assert.NotNull(second.Changes[0].Workout.DeletedAt);
        Assert.Equal(JsonSerializer.Serialize(second), JsonSerializer.Serialize(replay));
    }

    private async Task AddChangeAsync(Guid ownerId, Guid operationId, SyncWorkoutDto graph, DateTimeOffset changedAt)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.SyncChanges.Add(SyncChange.Create(
            ownerId, operationId, graph.Id, graph.Version, graph.DeletedAt is not null,
            JsonSerializer.Serialize(graph, new JsonSerializerOptions(JsonSerializerDefaults.Web)), changedAt));
        await database.SaveChangesAsync();
    }

    private static SyncWorkoutDto Graph(Guid id, bool deleted) => new(
        id, 3, Utc(7), Utc(8), deleted ? Utc(9) : null, deleted ? 3 : 2, []);

    private async Task<Guid> OwnerOfAsync(Guid entityId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SyncChanges
            .Where(item => item.EntityId == entityId).Select(item => item.OwnerId).SingleAsync();
    }

    private async Task PushAsync(string token, object operation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sync/push")
        { Content = JsonContent.Create(new { operations = new[] { operation } }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<SyncPullResponse> PullAsync(string token, string? cursor, int pageSize)
    {
        using var response = await PullResponseAsync(token, cursor, pageSize);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SyncPullResponse>())!;
    }

    private Task<HttpResponseMessage> PullResponseAsync(string token, string? cursor, int pageSize)
    {
        var path = $"/api/v1/sync/pull?pageSize={pageSize}" +
            (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync()
    {
        var email = $"pull-{Guid.NewGuid():N}@example.com";
        var registrationResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        { email, password = "ValidPassword!42" });
        var registration = await registrationResponse.Content.ReadFromJsonAsync<RegistrationResponse>();
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        { email, password = "ValidPassword!42", deviceName = "sync-pull-tests" });
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        return (registration!.UserId, token!.AccessToken);
    }

    private async Task SeedAsync(ExerciseDefinition exercise)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.Exercises.Add(exercise);
        await database.SaveChangesAsync();
    }

    private static DateTimeOffset Utc(int hour) => new(2026, 8, 16, hour, 0, 0, TimeSpan.Zero);

    private static void SetEnvironment(string? connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", connectionString);
        Environment.SetEnvironmentVariable("Jwt__Issuer", connectionString is null ? null : "trackz-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", connectionString is null ? null : "trackz-mobile");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", connectionString is null ? null : "test-signing-key-that-is-at-least-thirty-two-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", connectionString is null ? null : "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", connectionString is null ? null : "14");
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class SyncPullApiFactory(string connectionString) : WebApplicationFactory<Program>
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
