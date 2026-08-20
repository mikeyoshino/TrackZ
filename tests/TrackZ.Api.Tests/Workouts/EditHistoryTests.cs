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
using TrackZ.Infrastructure.Persistence;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
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
        var concurrentCompletions = await Task.WhenAll(
            PushDocumentAsync(owner.Token, complete),
            PushDocumentAsync(owner.Token, complete));
        AssertResult(concurrentCompletions[0], "Applied", 4, null);
        Assert.Equal(Result(concurrentCompletions[0]).GetRawText(),
            Result(concurrentCompletions[1]).GetRawText());

        await using (var projectionScope = _factory!.Services.CreateAsyncScope())
        {
            var projectionStore = projectionScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var performance = await projectionStore.ExercisePerformances.AsNoTracking().SingleAsync(item =>
                item.UserId == owner.UserId && item.ExerciseDefinitionId == exercise.Id);
            Assert.Equal(startedAt.AddMinutes(10), performance.LastPerformedAt);
            Assert.Equal(72.5m, performance.LastBestWeightKg);
            Assert.Equal(8, performance.LastBestReps);
            Assert.Equal(72.5m, performance.AllTimeBestWeightKg);
            Assert.Equal(8, performance.AllTimeBestReps);
        }

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
        await AssertPerformanceAsync(
            owner.UserId, exercise.Id, startedAt.AddMinutes(10), 75.125m, 6, 75.125m, 6);

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
        await AssertPerformanceAsync(
            owner.UserId, exercise.Id, startedAt.AddMinutes(10), 72.5m, 8, 72.5m, 8);

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
        Assert.Empty(await database.ExercisePerformances.Where(performance =>
            performance.UserId == owner.UserId
            && performance.ExerciseDefinitionId == exercise.Id).ToListAsync());
    }

    [Fact]
    public async Task Deleting_latest_sets_and_workout_falls_back_to_prior_completed_session_projection()
    {
        var owner = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Projection Fallback Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);

        var prior = new SyncIds(Guid.NewGuid(), Guid.NewGuid());
        var priorAt = Utc(6);
        await AssertAppliedAsync(owner.Token, Start(prior, exercise.Id, priorAt), 1);
        await AssertAppliedAsync(owner.Token, SaveSet(
            prior, Guid.NewGuid(), Guid.NewGuid(), 1, 0, "60", 12, priorAt.AddMinutes(1)), 2);
        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "CompleteWorkout", 2, new
        {
            workoutId = prior.WorkoutId,
            completedAt = priorAt.AddMinutes(10)
        }), 3);

        var latest = new SyncIds(Guid.NewGuid(), Guid.NewGuid());
        var latestAt = Utc(7);
        var latestSetId = Guid.NewGuid();
        await AssertAppliedAsync(owner.Token, Start(latest, exercise.Id, latestAt), 1);
        await AssertAppliedAsync(owner.Token, SaveSet(
            latest, Guid.NewGuid(), latestSetId, 1, 0, "80", 5, latestAt.AddMinutes(1)), 2);
        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "CompleteWorkout", 2, new
        {
            workoutId = latest.WorkoutId,
            completedAt = latestAt.AddMinutes(10)
        }), 3);
        await AssertPerformanceAsync(
            owner.UserId, exercise.Id, latestAt.AddMinutes(10), 80m, 5, 80m, 5);

        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "DeleteSet", 3, new
        {
            workoutId = latest.WorkoutId,
            workoutExerciseId = latest.WorkoutExerciseId,
            setId = latestSetId,
            deletedAt = latestAt.AddMinutes(11)
        }), 4);
        await AssertPerformanceAsync(
            owner.UserId, exercise.Id, priorAt.AddMinutes(10), 60m, 12, 60m, 12);

        var newest = new SyncIds(Guid.NewGuid(), Guid.NewGuid());
        var newestAt = Utc(8);
        await AssertAppliedAsync(owner.Token, Start(newest, exercise.Id, newestAt), 1);
        await AssertAppliedAsync(owner.Token, SaveSet(
            newest, Guid.NewGuid(), Guid.NewGuid(), 1, 0, "90", 3, newestAt.AddMinutes(1)), 2);
        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "CompleteWorkout", 2, new
        {
            workoutId = newest.WorkoutId,
            completedAt = newestAt.AddMinutes(10)
        }), 3);
        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "DeleteWorkout", 3, new
        {
            workoutId = newest.WorkoutId,
            deletedAt = newestAt.AddMinutes(11)
        }), 4);
        await AssertPerformanceAsync(
            owner.UserId, exercise.Id, priorAt.AddMinutes(10), 60m, 12, 60m, 12);
    }

    [Theory]
    [InlineData(TrackingMode.Bodyweight)]
    [InlineData(TrackingMode.Assisted)]
    public async Task Completion_materializes_exact_mode_specific_last_and_pr(TrackingMode mode)
    {
        var owner = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Projection {mode} {Guid.NewGuid():N}", BodyPart.Chest, mode);
        await SeedAsync(exercise);
        var ids = new SyncIds(Guid.NewGuid(), Guid.NewGuid());
        var startedAt = Utc(9);
        await AssertAppliedAsync(owner.Token, Start(ids, exercise.Id, startedAt, mode), 1);
        await AssertAppliedAsync(owner.Token, SaveSetForMode(
            ids, Guid.NewGuid(), Guid.NewGuid(), 1, 0,
            null, mode == TrackingMode.Assisted ? "25" : null, 10,
            startedAt.AddMinutes(1)), 2);
        await AssertAppliedAsync(owner.Token, SaveSetForMode(
            ids, Guid.NewGuid(), Guid.NewGuid(), 2, 1,
            null, mode == TrackingMode.Assisted ? "20" : null, 12,
            startedAt.AddMinutes(2)), 3);
        await AssertAppliedAsync(owner.Token, Operation(Guid.NewGuid(), "CompleteWorkout", 3, new
        {
            workoutId = ids.WorkoutId,
            completedAt = startedAt.AddMinutes(10)
        }), 4);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var performance = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ExercisePerformances.AsNoTracking().SingleAsync(item =>
                item.UserId == owner.UserId && item.ExerciseDefinitionId == exercise.Id);
        Assert.Equal(startedAt.AddMinutes(10), performance.LastPerformedAt);
        Assert.Equal(12, performance.LastBestReps);
        Assert.Equal(12, performance.AllTimeBestReps);
        Assert.Equal(mode == TrackingMode.Assisted ? 20m : null, performance.LastBestAssistedKg);
        Assert.Equal(mode == TrackingMode.Assisted ? 20m : null, performance.AllTimeBestAssistedKg);
        Assert.Null(performance.LastBestWeightKg);
        Assert.Null(performance.AllTimeBestWeightKg);
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
        AssertResult(foreign, "Rejected", null, 30001);

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
        await AssertPerformanceAsync(
            owner.UserId, exercise.Id, startedAt.AddMinutes(10), 70m, 10, 70m, 10);
        await using var scope = _factory!.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ExercisePerformances.AnyAsync(item =>
                item.UserId == other.UserId && item.ExerciseDefinitionId == exercise.Id));
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
    public async Task Offline_log_kill_recreate_commit_then_drop_replays_same_completion_exactly_once_to_postgresql()
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
            var completeOperation = (await new OutboxRepository(new TrackZLocalDatabase(sqlitePath))
                    .PendingAsync())
                .Single(operation => operation.Type == OutboxOperationType.CompleteWorkout);
            using var mobileHttp = _factory!.CreateClient();
            mobileHttp.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", owner.Token);
            var dropAfterCommit = new CommitThenDropOnActionApi(
                new TrackZSyncApiClient(mobileHttp), "CompleteWorkout");
            var ambiguousSync = new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath),
                dropAfterCommit,
                boundary,
                clock);

            Assert.Equal(SyncRunStatus.Offline, await ambiguousSync.RunOnceAsync());
            Assert.Equal(completeOperation.OperationId, dropAfterCommit.DroppedOperationId);
            var ambiguous = await new LocalWorkoutRepository(new TrackZLocalDatabase(sqlitePath))
                .GetOperationAsync(completeOperation.OperationId);
            Assert.NotNull(ambiguous!.SendStartedAt);

            // Kill/recreate every SQLite/coordinator object and replay the exact persisted operation ID.
            var recreatedBoundary = new AccountSessionBoundary();
            var sync = new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath),
                new TrackZSyncApiClient(mobileHttp),
                recreatedBoundary,
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

            // Exercise the same ambiguous boundary for a durable historical edit.
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var localExercise = Assert.Single(local.Exercises);
            var edit = await new WorkoutHistoryCoordinator(
                    new LocalWorkoutRepository(new TrackZLocalDatabase(sqlitePath)),
                    recreatedBoundary,
                    clock)
                .EditSetAsync(
                    local.Id,
                    localExercise.Id,
                    first.Id,
                    new HistorySetMeasurement(75.125m, null, 6));
            var dropAfterEdit = new CommitThenDropOnActionApi(
                new TrackZSyncApiClient(mobileHttp), "EditSet");
            Assert.Equal(SyncRunStatus.Offline, await new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath), dropAfterEdit, recreatedBoundary, clock)
                .RunOnceAsync());
            Assert.Equal(edit.OperationId, dropAfterEdit.DroppedOperationId);
            Assert.Equal(SyncRunStatus.Completed, await new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath),
                new TrackZSyncApiClient(mobileHttp),
                new AccountSessionBoundary(),
                clock).RunOnceAsync());

            // A real tombstone is then pulled into an independent second-device cache.
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var deletion = await new WorkoutHistoryCoordinator(
                    new LocalWorkoutRepository(new TrackZLocalDatabase(sqlitePath)),
                    recreatedBoundary,
                    clock)
                .DeleteSetAsync(local.Id, localExercise.Id, first.Id);
            Assert.NotEqual(Guid.Empty, deletion.OperationId);
            Assert.Equal(SyncRunStatus.Completed, await new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath),
                new TrackZSyncApiClient(mobileHttp),
                recreatedBoundary,
                clock).RunOnceAsync());

            var secondDevicePath = Path.Combine(
                Path.GetTempPath(), $"trackz-real-sync-second-{Guid.NewGuid():N}.db");
            try
            {
                Assert.Equal(SyncRunStatus.Completed, await new SyncCoordinator(
                    new TrackZLocalDatabase(secondDevicePath),
                    new TrackZSyncApiClient(mobileHttp),
                    new AccountSessionBoundary(),
                    clock).RunOnceAsync());
                var secondDeviceHistory = Assert.Single(await new LocalWorkoutRepository(
                    new TrackZLocalDatabase(secondDevicePath)).GetHistoryAsync());
                var secondDeviceSet = Assert.Single(Assert.Single(secondDeviceHistory.Exercises).Sets);
                Assert.Equal(second.Id, secondDeviceSet.Id);
                Assert.Equal(8, secondDeviceSet.Reps);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
                {
                    var path = secondDevicePath + suffix;
                    if (File.Exists(path)) File.Delete(path);
                }
            }

            await using var scope = _factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var completedExerciseId = Assert.Single(completed.Exercises).Id;
            Assert.Single(await store.WorkoutSessions.Where(item => item.Id == completed.Id).ToListAsync());
            Assert.Equal(2, await store.SetEntries.CountAsync(item =>
                item.WorkoutExerciseId == completedExerciseId));
            Assert.Equal(6, await store.ProcessedClientOperations.CountAsync(item =>
                item.UserId == owner.UserId));
            Assert.Single(await store.ProcessedClientOperations.Where(item =>
                item.UserId == owner.UserId
                && item.OperationId == completeOperation.OperationId).ToListAsync());
            var performance = await store.ExercisePerformances.AsNoTracking().SingleAsync(item =>
                item.UserId == owner.UserId && item.ExerciseDefinitionId == exercise.Id);
            Assert.Equal(completed.CompletedAt, performance.LastPerformedAt);
            Assert.Equal(72.5m, performance.LastBestWeightKg);
            Assert.Equal(8, performance.LastBestReps);
            Assert.Equal(72.5m, performance.AllTimeBestWeightKg);
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

    [Fact]
    public async Task Ambiguous_rebased_edit_commit_blocks_stale_keep_server_until_exact_replay_and_pull()
    {
        var owner = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Ambiguous Conflict Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var sqlitePath = Path.Combine(
            Path.GetTempPath(), $"trackz-ambiguous-conflict-{Guid.NewGuid():N}.db");
        try
        {
            var clock = new TestClock(Utc(16));
            var boundary = new AccountSessionBoundary();
            var database = new TrackZLocalDatabase(sqlitePath);
            var active = new ActiveWorkoutCoordinator(
                new LocalWorkoutRepository(database), boundary, clock);
            await active.StartAsync([
                new WorkoutExerciseSelection(exercise.Id, TrackingMode.Weighted)
            ]);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var set = await active.SaveSetAsync(exercise.Id, new LocalSet(70m, null, 10));
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            var completed = await active.FinishAsync();
            var workoutExercise = Assert.Single(completed.Exercises);
            using var mobileHttp = _factory!.CreateClient();
            mobileHttp.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", owner.Token);
            var api = new TrackZSyncApiClient(mobileHttp);
            var sync = new SyncCoordinator(database, api, boundary, clock);
            Assert.Equal(SyncRunStatus.Completed, await sync.RunOnceAsync());

            var remoteUpdatedAt = clock.UtcNow.AddMinutes(1);
            await AssertAppliedAsync(owner.Token, Operation(
                Guid.NewGuid(),
                "EditSet",
                3,
                new
                {
                    workoutId = completed.Id,
                    workoutExerciseId = workoutExercise.Id,
                    setId = set.Id,
                    weightKg = "65",
                    assistedKg = (string?)null,
                    reps = 9,
                    updatedAt = remoteUpdatedAt
                }), 4);

            clock.UtcNow = remoteUpdatedAt.AddMinutes(1);
            var localEdit = await new WorkoutHistoryCoordinator(
                    new LocalWorkoutRepository(database), boundary, clock)
                .EditSetAsync(
                    completed.Id,
                    workoutExercise.Id,
                    set.Id,
                    new HistorySetMeasurement(75m, null, 6));
            Assert.Equal(SyncRunStatus.Completed, await sync.RunOnceAsync());
            var conflict = Assert.Single(await new OutboxRepository(database).ConflictedAsync());
            Assert.Equal(localEdit.OperationId, conflict.OperationId);
            Assert.Equal(4, conflict.ServerVersion);
            var replacement = await new ConflictResolution(sync)
                .ApplyLocalAgainstVersionAsync(conflict.OperationId, 4);

            var commitThenDrop = new CommitThenDropOnActionApi(api, "EditSet");
            Assert.Equal(SyncRunStatus.Offline, await new SyncCoordinator(
                database, commitThenDrop, boundary, clock).RunOnceAsync());
            Assert.Equal(replacement.OperationId, commitThenDrop.DroppedOperationId);
            var restartedDatabase = new TrackZLocalDatabase(sqlitePath);
            var restartedRepository = new LocalWorkoutRepository(restartedDatabase);
            var beforeOperations = await new OutboxRepository(restartedDatabase)
                .ForHistoryWorkoutAsync(completed.Id);
            var beforeGraph = Assert.Single(await restartedRepository.GetHistoryAsync());
            var beforeSnapshot = await ReadHistoryUndoSnapshotAsync(
                sqlitePath, replacement.OperationId);
            Assert.NotNull(beforeSnapshot);
            var ambiguousReplacement = await restartedRepository
                .GetOperationAsync(replacement.OperationId);
            Assert.Equal(OutboxOperationState.Pending, ambiguousReplacement!.State);
            Assert.NotNull(ambiguousReplacement.SendStartedAt);
            Assert.Equal(6, Assert.Single(Assert.Single(beforeGraph.Exercises).Sets).Reps);

            var restartedBoundary = new AccountSessionBoundary();
            var restartedSync = new SyncCoordinator(
                restartedDatabase, api, restartedBoundary, clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new ConflictResolution(restartedSync).KeepServerAsync(conflict.OperationId));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new WorkoutHistoryCoordinator(restartedRepository, restartedBoundary, clock)
                    .UndoAsync(replacement.OperationId));

            Assert.Equal(beforeOperations, await new OutboxRepository(restartedDatabase)
                .ForHistoryWorkoutAsync(completed.Id));
            Assert.Equal(beforeSnapshot, await ReadHistoryUndoSnapshotAsync(
                sqlitePath, replacement.OperationId));
            Assert.Equal(
                JsonSerializer.Serialize(beforeGraph),
                JsonSerializer.Serialize(Assert.Single(await restartedRepository.GetHistoryAsync())));
            var serverBeforeReplay = await ReadJsonAsync(owner.Token, "/api/v1/workouts");
            var committedServerWorkout = Assert.Single(
                serverBeforeReplay.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(5, committedServerWorkout.GetProperty("version").GetInt64());
            Assert.Equal(6, committedServerWorkout.GetProperty("exercises")[0]
                .GetProperty("sets")[0].GetProperty("reps").GetInt32());

            Assert.Equal(SyncRunStatus.Completed, await new SyncCoordinator(
                new TrackZLocalDatabase(sqlitePath), api,
                new AccountSessionBoundary(), clock).RunOnceAsync());
            var reconciledRepository = new LocalWorkoutRepository(
                new TrackZLocalDatabase(sqlitePath));
            var reconciled = Assert.Single(await reconciledRepository.GetHistoryAsync());
            Assert.Equal(5, reconciled.Version);
            Assert.Equal(6, Assert.Single(Assert.Single(reconciled.Exercises).Sets).Reps);
            Assert.Empty(await new OutboxRepository(new TrackZLocalDatabase(sqlitePath))
                .ForHistoryWorkoutAsync(completed.Id));
            Assert.Null(await ReadHistoryUndoSnapshotAsync(sqlitePath, replacement.OperationId));

            await using var scope = _factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await store.ProcessedClientOperations.Where(item =>
                item.UserId == owner.UserId
                && item.OperationId == replacement.OperationId).ToListAsync());
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

    private static object Start(
        SyncIds ids,
        Guid exerciseId,
        DateTimeOffset startedAt,
        TrackingMode trackingMode = TrackingMode.Weighted) =>
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
                    trackingMode = (int)trackingMode,
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

    private static object SaveSetForMode(
        SyncIds ids,
        Guid operationId,
        Guid setId,
        long baseVersion,
        int order,
        string? weightKg,
        string? assistedKg,
        int reps,
        DateTimeOffset completedAt) => Operation(operationId, "SaveSet", baseVersion, new
        {
            workoutId = ids.WorkoutId,
            workoutExerciseId = ids.WorkoutExerciseId,
            setId,
            order,
            weightKg,
            assistedKg,
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

    private async Task AssertPerformanceAsync(
        Guid ownerId,
        Guid exerciseId,
        DateTimeOffset lastPerformedAt,
        decimal lastWeightKg,
        int lastReps,
        decimal allTimeWeightKg,
        int allTimeReps)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var performance = await store.ExercisePerformances.AsNoTracking().SingleAsync(item =>
            item.UserId == ownerId && item.ExerciseDefinitionId == exerciseId);
        Assert.Equal(lastPerformedAt, performance.LastPerformedAt);
        Assert.Equal(lastWeightKg, performance.LastBestWeightKg);
        Assert.Equal(lastReps, performance.LastBestReps);
        Assert.Equal(allTimeWeightKg, performance.AllTimeBestWeightKg);
        Assert.Equal(allTimeReps, performance.AllTimeBestReps);
    }

    private static async Task<string?> ReadHistoryUndoSnapshotAsync(
        string sqlitePath,
        Guid operationId)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={sqlitePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SnapshotJson FROM HistoryUndo WHERE OperationId = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return (string?)await command.ExecuteScalarAsync();
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

    private sealed class CommitThenDropOnActionApi(ISyncApi inner, string action) : ISyncApi
    {
        private bool _dropped;

        public Guid? DroppedOperationId { get; private set; }

        public async Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.PushAsync(request, cancellationToken);
            var operation = request.Operations.Single();
            if (!_dropped && operation.Action == action)
            {
                _dropped = true;
                DroppedOperationId = operation.OperationId;
                throw new HttpRequestException(
                    "The committed server response was lost at the transport boundary.");
            }

            return response;
        }

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default) =>
            inner.PullAsync(cursor, cancellationToken);
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
