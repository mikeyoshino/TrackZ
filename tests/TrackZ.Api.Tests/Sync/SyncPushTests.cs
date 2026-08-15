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
using Xunit.Sdk;

namespace TrackZ.Api.Tests.Sync;

public sealed class SyncPushTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private SyncApiFactory? _factory;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_sync_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_sync_tests_only")
            .Build();
        try
        {
            await _container.StartAsync();
        }
        catch (DockerUnavailableException exception)
        {
            await _container.DisposeAsync();
            throw SkipException.ForSkip($"Docker is unavailable; sync API tests require Docker. {exception.Message}");
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__TrackZ", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", "trackz-api");
        Environment.SetEnvironmentVariable("Jwt__Audience", "trackz-mobile");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "14");
        _factory = new SyncApiFactory(_container.GetConnectionString());
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync();
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
    public async Task Retrying_same_operation_returns_prior_result_without_duplicate_set()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Sync Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var workoutId = Guid.NewGuid();
        var workoutExerciseId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero);
        var start = Operation(Guid.NewGuid(), "StartWorkout", 0, new
        {
            workoutId,
            startedAt,
            exercises = new[]
            {
                new { workoutExerciseId, exerciseDefinitionId = exercise.Id, trackingMode = 1, order = 0 }
            }
        });
        var startResponse = await PushAsync(authentication.Token, start);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        var setId = Guid.NewGuid();
        var saveSet = Operation(Guid.NewGuid(), "SaveSet", 1, new
        {
            workoutId,
            workoutExerciseId,
            setId,
            order = 0,
            weightKg = "70",
            assistedKg = (string?)null,
            reps = 10,
            completedAt = startedAt.AddMinutes(1)
        });
        var firstResponse = await PushAsync(authentication.Token, saveSet);
        var secondResponse = await PushAsync(authentication.Token, saveSet);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonDocument>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(
            first!.RootElement.GetProperty("results")[0].GetRawText(),
            second!.RootElement.GetProperty("results")[0].GetRawText());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await database.SetEntries.CountAsync(set => set.Id == setId));
    }

    [Fact]
    public async Task Reusing_operation_id_with_altered_request_is_rejected_without_replacing_prior_result()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Immutable Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(8);
        await AssertAppliedAsync(authentication.Token, StartOperation(ids, exercise.Id, startedAt), 1);
        var operationId = Guid.NewGuid();
        var original = SaveSetOperation(ids, operationId, Guid.NewGuid(), 1, 0, "70", 10, startedAt.AddMinutes(1));
        var first = await PushDocumentAsync(authentication.Token, original);
        var altered = SaveSetOperation(ids, operationId, Guid.NewGuid(), 1, 1, "80", 5, startedAt.AddMinutes(2));
        var mismatch = await PushDocumentAsync(authentication.Token, altered);
        var replay = await PushDocumentAsync(authentication.Token, original);

        AssertResult(first, 0, "Applied", 2, null);
        AssertResult(mismatch, 0, "Rejected", null, 10009);
        Assert.Equal(Result(first, 0).GetRawText(), Result(replay, 0).GetRawText());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await database.SetEntries.Where(set => set.WorkoutExerciseId == ids.WorkoutExerciseId).ToListAsync());
        var stored = await database.ProcessedClientOperations.SingleAsync(item =>
            item.UserId == authentication.UserId && item.OperationId == operationId);
        using var storedResult = JsonDocument.Parse(stored.ResultJson);
        Assert.Equal(operationId, storedResult.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("Applied", storedResult.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, storedResult.RootElement.GetProperty("serverVersion").GetInt64());
    }

    [Fact]
    public async Task Batch_preserves_input_order_and_isolates_unknown_malformed_and_applied_operations()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Batch Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(9);
        var unknown = Operation(Guid.NewGuid(), "UnknownWorkoutAction", 0, new { workoutId = ids.WorkoutId });
        var malformed = Operation(Guid.NewGuid(), "SaveSet", 1, new { workoutId = ids.WorkoutId });
        var save = SaveSetOperation(ids, Guid.NewGuid(), Guid.NewGuid(), 1, 0, "72.5", 8, startedAt.AddMinutes(1));

        var response = await PushDocumentAsync(
            authentication.Token,
            StartOperation(ids, exercise.Id, startedAt),
            unknown,
            malformed,
            save);

        AssertResult(response, 0, "Applied", 1, null);
        AssertResult(response, 1, "Rejected", null, 10009);
        AssertResult(response, 2, "Rejected", null, 10009);
        AssertResult(response, 3, "Applied", 2, null);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await database.ProcessedClientOperations.CountAsync(item => item.UserId == authentication.UserId));
        Assert.Single(await database.SetEntries.Where(set => set.WorkoutExerciseId == ids.WorkoutExerciseId).ToListAsync());
    }

    [Fact]
    public async Task Null_and_missing_payload_entries_are_rejected_without_aborting_later_operations()
    {
        var authentication = await AuthenticateAsync();
        var missingPayloadId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        var missingPayload = new
        {
            operationId = missingPayloadId,
            entityType = "Workout",
            action = "SaveSet",
            baseVersion = 0
        };
        var nullExerciseId = Guid.NewGuid();
        var nullExercise = Operation(nullExerciseId, "StartWorkout", 0, new
        {
            workoutId = Guid.NewGuid(),
            startedAt = Utc(7),
            exercises = new object?[] { null }
        });
        var later = Operation(laterId, "UnknownWorkoutAction", 0, new { });

        var response = await PushDocumentAsync(
            authentication.Token,
            null,
            missingPayload,
            nullExercise,
            later);

        AssertResult(response, 0, "Rejected", null, 10009);
        Assert.Equal(Guid.Empty, Result(response, 0).GetProperty("operationId").GetGuid());
        AssertResult(response, 1, "Rejected", null, 10009);
        AssertResult(response, 2, "Rejected", null, 10009);
        AssertResult(response, 3, "Rejected", null, 10009);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await database.ProcessedClientOperations.CountAsync(item => item.UserId == authentication.UserId));
    }

    [Fact]
    public async Task Empty_operation_identifier_is_rejected_before_mutating_workout_state()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Empty Operation Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(16);
        var operation = Operation(Guid.Empty, "StartWorkout", 0, new
        {
            workoutId = ids.WorkoutId,
            startedAt,
            exercises = new[]
            {
                new
                {
                    workoutExerciseId = ids.WorkoutExerciseId,
                    exerciseDefinitionId = exercise.Id,
                    trackingMode = 1,
                    order = 0
                }
            }
        });

        var response = await PushDocumentAsync(authentication.Token, operation);

        AssertResult(response, 0, "Rejected", null, 10009);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.WorkoutSessions.AnyAsync(workout => workout.Id == ids.WorkoutId));
    }

    [Fact]
    public async Task Omitted_required_zero_valued_fields_are_rejected_instead_of_defaulted()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Required Field Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var startedAt = Utc(17);
        var missingBaseIds = SyncIds.Create();
        var missingBaseVersion = new
        {
            operationId = Guid.NewGuid(),
            entityType = "Workout",
            action = "StartWorkout",
            payload = new
            {
                workoutId = missingBaseIds.WorkoutId,
                startedAt,
                exercises = new[]
                {
                    new
                    {
                        workoutExerciseId = missingBaseIds.WorkoutExerciseId,
                        exerciseDefinitionId = exercise.Id,
                        trackingMode = 1,
                        order = 0
                    }
                }
            }
        };
        var missingExerciseOrderIds = SyncIds.Create();
        var missingExerciseOrder = Operation(Guid.NewGuid(), "StartWorkout", 0, new
        {
            workoutId = missingExerciseOrderIds.WorkoutId,
            startedAt,
            exercises = new[]
            {
                new
                {
                    workoutExerciseId = missingExerciseOrderIds.WorkoutExerciseId,
                    exerciseDefinitionId = exercise.Id,
                    trackingMode = 1
                }
            }
        });
        var validIds = SyncIds.Create();
        var missingSetOrder = Operation(Guid.NewGuid(), "SaveSet", 1, new
        {
            workoutId = validIds.WorkoutId,
            workoutExerciseId = validIds.WorkoutExerciseId,
            setId = Guid.NewGuid(),
            weightKg = "70",
            assistedKg = (string?)null,
            reps = 10,
            completedAt = startedAt.AddMinutes(1)
        });
        var missingAssistedField = Operation(Guid.NewGuid(), "SaveSet", 1, new
        {
            workoutId = validIds.WorkoutId,
            workoutExerciseId = validIds.WorkoutExerciseId,
            setId = Guid.NewGuid(),
            order = 0,
            weightKg = "70",
            reps = 10,
            completedAt = startedAt.AddMinutes(1)
        });

        var response = await PushDocumentAsync(
            authentication.Token,
            missingBaseVersion,
            missingExerciseOrder,
            StartOperation(validIds, exercise.Id, startedAt),
            missingSetOrder,
            missingAssistedField);

        AssertResult(response, 0, "Rejected", null, 10009);
        AssertResult(response, 1, "Rejected", null, 10009);
        AssertResult(response, 2, "Applied", 1, null);
        AssertResult(response, 3, "Rejected", null, 10009);
        AssertResult(response, 4, "Rejected", null, 10009);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workoutIds = await database.WorkoutSessions.Select(workout => workout.Id).ToListAsync();
        Assert.Equal([validIds.WorkoutId], workoutIds);
        Assert.Empty(await database.SetEntries.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_and_gapped_start_orders_are_terminal_rejections_without_workout_mutation()
    {
        var authentication = await AuthenticateAsync();
        var firstExercise = ExerciseDefinition.CreateSystem(
            $"Order Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        var secondExercise = ExerciseDefinition.CreateSystem(
            $"Order Row {Guid.NewGuid():N}", BodyPart.Back, TrackingMode.Weighted);
        await SeedAsync(firstExercise, secondExercise);
        var startedAt = Utc(18);
        var duplicateWorkoutId = Guid.NewGuid();
        var duplicate = Operation(Guid.NewGuid(), "StartWorkout", 0, new
        {
            workoutId = duplicateWorkoutId,
            startedAt,
            exercises = new[]
            {
                new
                {
                    workoutExerciseId = Guid.NewGuid(),
                    exerciseDefinitionId = firstExercise.Id,
                    trackingMode = 1,
                    order = 0
                },
                new
                {
                    workoutExerciseId = Guid.NewGuid(),
                    exerciseDefinitionId = secondExercise.Id,
                    trackingMode = 1,
                    order = 0
                }
            }
        });
        var gappedWorkoutId = Guid.NewGuid();
        var gapped = Operation(Guid.NewGuid(), "StartWorkout", 0, new
        {
            workoutId = gappedWorkoutId,
            startedAt,
            exercises = new[]
            {
                new
                {
                    workoutExerciseId = Guid.NewGuid(),
                    exerciseDefinitionId = firstExercise.Id,
                    trackingMode = 1,
                    order = 0
                },
                new
                {
                    workoutExerciseId = Guid.NewGuid(),
                    exerciseDefinitionId = secondExercise.Id,
                    trackingMode = 1,
                    order = 2
                }
            }
        });

        var first = await PushDocumentAsync(authentication.Token, duplicate, gapped);
        var replay = await PushDocumentAsync(authentication.Token, duplicate);

        AssertResult(first, 0, "Rejected", null, 10009);
        AssertResult(first, 1, "Rejected", null, 10009);
        Assert.Equal(Result(first, 0).GetRawText(), Result(replay, 0).GetRawText());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.WorkoutSessions.AnyAsync(workout =>
            workout.Id == duplicateWorkoutId || workout.Id == gappedWorkoutId));
        var processed = await database.ProcessedClientOperations
            .Where(operation => operation.UserId == authentication.UserId)
            .ToListAsync();
        Assert.Equal(2, processed.Count);
        Assert.All(processed, operation => Assert.Contains("\"Rejected\"", operation.ResultJson));
    }

    [Fact]
    public async Task Conflict_replay_keeps_original_server_version_after_later_mutations()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Conflict Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(10);
        await AssertAppliedAsync(authentication.Token, StartOperation(ids, exercise.Id, startedAt), 1);
        var conflict = SaveSetOperation(ids, Guid.NewGuid(), Guid.NewGuid(), 0, 0, "70", 10, startedAt.AddMinutes(1));
        var firstConflict = await PushDocumentAsync(authentication.Token, conflict);
        var applied = SaveSetOperation(ids, Guid.NewGuid(), Guid.NewGuid(), 1, 0, "70", 10, startedAt.AddMinutes(1));
        await AssertAppliedAsync(authentication.Token, applied, 2);
        var replayedConflict = await PushDocumentAsync(authentication.Token, conflict);

        AssertResult(firstConflict, 0, "Conflict", 1, 60001);
        Assert.Equal(Result(firstConflict, 0).GetRawText(), Result(replayedConflict, 0).GetRawText());
    }

    [Fact]
    public async Task Concurrent_duplicate_pushes_commit_one_set_and_replay_one_result()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Concurrent Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(11);
        await AssertAppliedAsync(authentication.Token, StartOperation(ids, exercise.Id, startedAt), 1);
        var operation = SaveSetOperation(
            ids, Guid.NewGuid(), Guid.NewGuid(), 1, 0, "75", 6, startedAt.AddMinutes(1));

        var responses = await Task.WhenAll(
            PushDocumentAsync(authentication.Token, operation),
            PushDocumentAsync(authentication.Token, operation));

        AssertResult(responses[0], 0, "Applied", 2, null);
        Assert.Equal(Result(responses[0], 0).GetRawText(), Result(responses[1], 0).GetRawText());
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await database.SetEntries.Where(set => set.WorkoutExerciseId == ids.WorkoutExerciseId).ToListAsync());
    }

    [Fact]
    public async Task Transient_database_failure_rolls_back_only_that_operation_and_does_not_record_it()
    {
        var authentication = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Rollback Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(12);
        await AssertAppliedAsync(authentication.Token, StartOperation(ids, exercise.Id, startedAt), 1);
        var firstSetId = Guid.NewGuid();
        var first = SaveSetOperation(ids, Guid.NewGuid(), firstSetId, 1, 0, "60", 12, startedAt.AddMinutes(1));
        var failingOperationId = Guid.NewGuid();
        var failingSetId = Guid.NewGuid();
        var failing = SaveSetOperation(ids, failingOperationId, failingSetId, 2, 1, "65", 10, startedAt.AddMinutes(2));
        var lastSetId = Guid.NewGuid();
        var last = SaveSetOperation(ids, Guid.NewGuid(), lastSetId, 2, 1, "67.5", 8, startedAt.AddMinutes(3));
        await InstallTransientProcessedOperationFailureAsync(failingOperationId);

        var response = await PushDocumentAsync(authentication.Token, first, failing, last);

        AssertResult(response, 0, "Applied", 2, null);
        AssertResult(response, 1, "Retryable", null, 90001);
        AssertResult(response, 2, "Applied", 3, null);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var setIds = await database.SetEntries
            .Where(set => set.WorkoutExerciseId == ids.WorkoutExerciseId)
            .OrderBy(set => set.Order)
            .Select(set => set.Id)
            .ToListAsync();
        Assert.Equal([firstSetId, lastSetId], setIds);
        Assert.False(await database.ProcessedClientOperations.AnyAsync(item =>
            item.UserId == authentication.UserId && item.OperationId == failingOperationId));
    }

    [Fact]
    public async Task Foreign_workout_is_rejected_without_mutation_or_server_version_disclosure()
    {
        var owner = await AuthenticateAsync();
        var intruder = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Owner Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var ids = SyncIds.Create();
        var startedAt = Utc(13);
        await AssertAppliedAsync(owner.Token, StartOperation(ids, exercise.Id, startedAt), 1);

        var response = await PushDocumentAsync(intruder.Token, SaveSetOperation(
            ids, Guid.NewGuid(), Guid.NewGuid(), 1, 0, "70", 10, startedAt.AddMinutes(1)));

        AssertResult(response, 0, "Rejected", null, 10009);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await database.SetEntries.Where(set => set.WorkoutExerciseId == ids.WorkoutExerciseId).ToListAsync());
    }

    [Fact]
    public async Task Foreign_set_identifier_collision_is_a_permanent_rejection_without_owner_data_leak()
    {
        var firstUser = await AuthenticateAsync();
        var secondUser = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Collision Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var firstIds = SyncIds.Create();
        var secondIds = SyncIds.Create();
        var startedAt = Utc(14);
        await AssertAppliedAsync(firstUser.Token, StartOperation(firstIds, exercise.Id, startedAt), 1);
        await AssertAppliedAsync(secondUser.Token, StartOperation(secondIds, exercise.Id, startedAt), 1);
        var foreignSetId = Guid.NewGuid();
        await AssertAppliedAsync(firstUser.Token, SaveSetOperation(
            firstIds, Guid.NewGuid(), foreignSetId, 1, 0, "70", 10, startedAt.AddMinutes(1)), 2);

        var response = await PushDocumentAsync(secondUser.Token, SaveSetOperation(
            secondIds, Guid.NewGuid(), foreignSetId, 1, 0, "80", 5, startedAt.AddMinutes(1)));

        AssertResult(response, 0, "Rejected", null, 10009);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await database.SetEntries
            .Where(set => set.WorkoutExerciseId == secondIds.WorkoutExerciseId)
            .ToListAsync());
    }

    [Fact]
    public async Task Foreign_workout_exercise_identifier_collision_is_a_permanent_rejection()
    {
        var firstUser = await AuthenticateAsync();
        var secondUser = await AuthenticateAsync();
        var exercise = ExerciseDefinition.CreateSystem(
            $"Exercise Collision Press {Guid.NewGuid():N}", BodyPart.Chest, TrackingMode.Weighted);
        await SeedAsync(exercise);
        var sharedWorkoutExerciseId = Guid.NewGuid();
        var firstIds = new SyncIds(Guid.NewGuid(), sharedWorkoutExerciseId);
        var secondIds = new SyncIds(Guid.NewGuid(), sharedWorkoutExerciseId);
        var startedAt = Utc(15);
        await AssertAppliedAsync(firstUser.Token, StartOperation(firstIds, exercise.Id, startedAt), 1);

        var response = await PushDocumentAsync(
            secondUser.Token,
            StartOperation(secondIds, exercise.Id, startedAt));

        AssertResult(response, 0, "Rejected", null, 10009);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await database.WorkoutSessions.AnyAsync(workout => workout.Id == secondIds.WorkoutId));
    }

    [Fact]
    public async Task Unauthenticated_push_is_rejected_before_any_operation_is_processed()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/sync/push", new
        {
            operations = new[] { Operation(Guid.NewGuid(), "UnknownWorkoutAction", 0, new { }) }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await database.ProcessedClientOperations.ToListAsync());
    }

    private static object Operation(Guid operationId, string action, long baseVersion, object payload) => new
    {
        operationId,
        entityType = "Workout",
        action,
        payload,
        baseVersion
    };

    private static object StartOperation(SyncIds ids, Guid exerciseDefinitionId, DateTimeOffset startedAt) =>
        Operation(Guid.NewGuid(), "StartWorkout", 0, new
        {
            workoutId = ids.WorkoutId,
            startedAt,
            exercises = new[]
            {
                new
                {
                    workoutExerciseId = ids.WorkoutExerciseId,
                    exerciseDefinitionId,
                    trackingMode = 1,
                    order = 0
                }
            }
        });

    private static object SaveSetOperation(
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

    private async Task<JsonDocument> PushDocumentAsync(string token, params object?[] operations)
    {
        var response = await PushAsync(token, operations);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonDocument>())!;
    }

    private async Task AssertAppliedAsync(string token, object operation, long serverVersion)
    {
        var response = await PushDocumentAsync(token, operation);
        AssertResult(response, 0, "Applied", serverVersion, null);
    }

    private static JsonElement Result(JsonDocument response, int index) =>
        response.RootElement.GetProperty("results")[index];

    private static void AssertResult(
        JsonDocument response,
        int index,
        string status,
        long? serverVersion,
        int? errorCode)
    {
        var result = Result(response, index);
        Assert.Equal(status, result.GetProperty("status").GetString());
        Assert.Equal(serverVersion, result.GetProperty("serverVersion").GetInt64OrNull());
        Assert.Equal(errorCode, result.GetProperty("errorCode").GetInt32OrNull());
    }

    private async Task InstallTransientProcessedOperationFailureAsync(Guid operationId)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sql = $$"""
            CREATE FUNCTION fail_selected_processed_operation() RETURNS trigger AS $failure$
            BEGIN
                IF NEW."OperationId" = '{{operationId:D}}'::uuid THEN
                    RAISE EXCEPTION 'simulated serialization failure' USING ERRCODE = '40001';
                END IF;
                RETURN NEW;
            END;
            $failure$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_selected_processed_operation
            BEFORE INSERT ON processed_client_operations
            FOR EACH ROW EXECUTE FUNCTION fail_selected_processed_operation();
            """;
        await database.Database.ExecuteSqlRawAsync(sql);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 16, hour, 0, 0, TimeSpan.Zero);

    private async Task<HttpResponseMessage> PushAsync(string token, params object?[] operations)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sync/push")
        {
            Content = JsonContent.Create(new { operations })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<(Guid UserId, string Token)> AuthenticateAsync()
    {
        var email = $"sync-{Guid.NewGuid():N}@example.com";
        var registrationResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "ValidPassword!42"
        });
        var registration = await registrationResponse.Content.ReadFromJsonAsync<RegistrationResponse>();
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "ValidPassword!42",
            deviceName = "sync-tests"
        });
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        return (registration!.UserId, token!.AccessToken);
    }

    private async Task SeedAsync(params ExerciseDefinition[] exercises)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Exercises.AddRangeAsync(exercises);
        await database.SaveChangesAsync();
    }

    private sealed record RegistrationResponse(Guid UserId, string Email);
    private sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
    private sealed record SyncIds(Guid WorkoutId, Guid WorkoutExerciseId)
    {
        public static SyncIds Create() => new(Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed class SyncApiFactory(string connectionString) : WebApplicationFactory<Program>
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

internal static class JsonElementNullableExtensions
{
    public static long? GetInt64OrNull(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt64();

    public static int? GetInt32OrNull(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();
}
