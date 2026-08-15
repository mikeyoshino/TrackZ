using System.Globalization;
using Microsoft.Data.Sqlite;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class ActiveWorkoutCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 8, 30, 0, TimeSpan.Zero);
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"trackz-workout-{Guid.NewGuid():N}.db");
    private readonly Guid _exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Save_set_persists_exact_workout_graph_and_typed_outbox_before_returning()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
        ]);

        var saved = await fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(70.125m, null, 10));

        var recreated = CreateFixture();
        var restored = await recreated.Coordinator.RestoreActiveAsync();
        var set = Assert.Single(Assert.Single(restored!.Exercises).Sets);
        Assert.Equal(saved.Id, set.Id);
        Assert.Equal(70.125m, set.WeightKg);
        Assert.Equal(10, set.Reps);
        Assert.Equal(TimeSpan.Zero, set.CompletedAt.Offset);

        var pending = await recreated.Outbox.PendingAsync();
        Assert.Equal(2, pending.Count);
        var operation = pending[1];
        Assert.Equal(saved.OperationId, operation.OperationId);
        Assert.Equal(OutboxOperationType.SaveSet, operation.Type);
        var payload = operation.DeserializePayload<SaveSetOutboxPayload>();
        Assert.Equal(saved.Id, payload.SetId);
        Assert.Equal("70.125", payload.WeightKg);
        Assert.Null(payload.AssistedKg);
        Assert.Equal(10, payload.Reps);
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, "82.375", null)]
    [InlineData(TrackingMode.Bodyweight, null, null)]
    [InlineData(TrackingMode.Assisted, null, "27.625")]
    public async Task Restore_preserves_every_tracking_mode_and_exact_decimal_text(
        TrackingMode mode,
        string? weight,
        string? assisted)
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, mode)]);
        await fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(
                weight is null ? null : decimal.Parse(weight, CultureInfo.InvariantCulture),
                assisted is null ? null : decimal.Parse(assisted, CultureInfo.InvariantCulture),
                12));

        var restored = await CreateFixture().Coordinator.RestoreActiveAsync();
        var exercise = Assert.Single(restored!.Exercises);
        var set = Assert.Single(exercise.Sets);
        Assert.Equal(mode, exercise.TrackingMode);
        Assert.Equal(weight, set.WeightKg?.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(assisted, set.AssistedKg?.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_workout_graph_and_operation_together()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await CreateOutboxFailureTriggerAsync();

        await Assert.ThrowsAsync<SqliteException>(() => fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(70m, null, 10)));

        var recreated = CreateFixture();
        var restored = await recreated.Coordinator.RestoreActiveAsync();
        Assert.Empty(Assert.Single(restored!.Exercises).Sets);
        Assert.Single(await recreated.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Replaying_same_operation_and_payload_is_idempotent_but_changed_contract_fails_closed()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var workout = (await fixture.Repository.GetActiveAsync(default))!;
        var operation = Assert.Single(await fixture.Outbox.PendingAsync());

        await fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, operation, default);
        Assert.Single(await fixture.Outbox.PendingAsync());

        var changed = operation with { Payload = operation.Payload + " " };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, changed, default));
        Assert.Single(await fixture.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Exact_duplicate_operation_returns_prior_success_without_writing_higher_version_graph()
    {
        var fixture = CreateFixture();
        var persisted = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
        ]);
        var operation = Assert.Single(await fixture.Outbox.PendingAsync());
        var divergent = persisted with
        {
            Version = persisted.Version + 1,
            StartedAt = persisted.StartedAt.AddMinutes(15)
        };
        await ExecuteRawAsync($"""
            UPDATE OutboxOperation
            SET State = 2, Version = 2
            WHERE OperationId = '{operation.OperationId:D}';
            """);

        await fixture.Repository.SaveWorkoutAndEnqueueAsync(divergent, operation, default);

        var restored = await fixture.Repository.GetActiveAsync(default);
        Assert.Equal(persisted.Version, restored!.Version);
        Assert.Equal(persisted.StartedAt, restored.StartedAt);
        Assert.Equal(OutboxOperationState.Applied,
            (await fixture.Repository.GetOperationAsync(operation.OperationId, default))!.State);
    }

    [Fact]
    public async Task Different_snapshot_at_same_local_version_fails_closed_and_rolls_back_new_operation()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var original = (await fixture.Repository.GetActiveAsync(default))!;
        var changed = original with { StartedAt = original.StartedAt.AddMinutes(5) };
        var operation = OutboxOperation.Create(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            changed.Id,
            OutboxOperationType.StartWorkout,
            new StartWorkoutOutboxPayload(changed.Id, changed.StartedAt, []),
            changed.BaseVersion,
            changed.StartedAt);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(changed, operation, default));

        Assert.Equal(original.StartedAt, (await fixture.Repository.GetActiveAsync(default))!.StartedAt);
        Assert.Single(await fixture.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Concurrent_saves_are_serialized_and_restore_contiguous_set_order()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);

        var first = new LocalSet(60m, null, 12);
        var second = new LocalSet(65m, null, 8);
        await Task.WhenAll(
            fixture.Coordinator.SaveSetAsync(_exerciseId, first),
            fixture.Coordinator.SaveSetAsync(_exerciseId, second));

        var restored = await CreateFixture().Coordinator.RestoreActiveAsync();
        var sets = Assert.Single(restored!.Exercises).Sets;
        Assert.Equal([0, 1], sets.Select(set => set.Order));
        Assert.Equal([first.Id, second.Id], sets.Select(set => set.Id));
    }

    [Fact]
    public async Task Offline_mutations_form_the_same_root_version_chain_the_server_aggregate_will_apply()
    {
        var secondExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fixture = CreateFixture();
        var started = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(secondExerciseId, TrackingMode.Bodyweight)
        ]);
        Assert.Equal(2, started.Version);

        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(60m, null, 12));
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(65m, null, 10));
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(70m, null, 8));

        var operations = await fixture.Outbox.PendingAsync();
        Assert.Equal(
            [0L, 2L, 3L, 4L],
            operations.Select(operation => operation.BaseVersion));
        var restored = await fixture.Repository.GetActiveAsync(default);
        Assert.Equal(5, restored!.Version);
        Assert.Equal(4, restored.Exercises.Single(item => item.ExerciseDefinitionId == _exerciseId).Version);
        Assert.Equal(1, restored.Exercises.Single(item => item.ExerciseDefinitionId == secondExerciseId).Version);
        Assert.All(restored.Exercises.SelectMany(item => item.Sets), set => Assert.Equal(1, set.Version));
    }

    [Fact]
    public async Task Pending_operations_follow_causal_versions_across_exercises_when_clock_does_not_advance()
    {
        var secondExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var highOperationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var lowOperationId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(secondExerciseId, TrackingMode.Bodyweight)
        ]);
        await fixture.Coordinator.SaveSetAsync(
            _exerciseId,
            new LocalSet(60m, null, 12) with { OperationId = highOperationId });

        var recreated = CreateFixture(Now.AddDays(-1));
        await recreated.Coordinator.SaveSetAsync(
            secondExerciseId,
            new LocalSet(null, null, 15) with { OperationId = lowOperationId });

        var pending = await recreated.Outbox.PendingAsync();
        Assert.Equal([0L, 2L, 3L], pending.Select(operation => operation.BaseVersion));
        Assert.Equal(
            [highOperationId, lowOperationId],
            pending.Where(operation => operation.Type == OutboxOperationType.SaveSet)
                .Select(operation => operation.OperationId));
        Assert.True(pending[0].CreatedAt < pending[1].CreatedAt);
        Assert.True(pending[1].CreatedAt < pending[2].CreatedAt);

        var restoredSets = (await recreated.Repository.GetActiveAsync(default))!
            .Exercises.SelectMany(exercise => exercise.Sets).ToDictionary(set => set.OperationId);
        Assert.Equal(pending[1].CreatedAt, restoredSets[highOperationId].CompletedAt);
        Assert.Equal(pending[2].CreatedAt, restoredSets[lowOperationId].CompletedAt);
    }

    [Fact]
    public async Task Durable_replacement_barrier_blocks_generic_graph_edit_and_new_start_without_partial_write()
    {
        var fixture = CreateFixture();
        var original = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)
        ]);
        var startOperation = Assert.Single(await fixture.Outbox.PendingAsync());
        var replacementId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
        await ExecuteRawAsync($"""
            INSERT INTO OutboxOperation
                (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
                 State, DeletedAt, Version, ReplacesOperationId)
            SELECT '{replacementId:D}', EntityId, OperationType, Payload, BaseVersion,
                   '{Now.AddTicks(1):O}', 1, NULL, 1, OperationId
            FROM OutboxOperation WHERE OperationId = '{startOperation.OperationId:D}';
            """);
        var edited = original with { Version = original.Version + 1 };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(
                edited, ReorderOperation(edited, original.Version, 1), default));

        Assert.Equal(original.Version, (await fixture.Repository.GetActiveAsync(default))!.Version);
        Assert.Equal(2, (await fixture.Outbox.PendingAsync()).Count);
        await ExecuteRawAsync($"""
            UPDATE LocalWorkout
            SET Status = 3, CompletedAt = '{Now.AddMinutes(2):O}', Version = Version + 1
            WHERE Id = '{original.Id:D}';
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.StartAsync([
                new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Bodyweight)
            ]));

        Assert.Null(await fixture.Repository.GetActiveAsync(default));
        Assert.Equal(2, (await fixture.Outbox.PendingAsync()).Count);
    }

    [Fact]
    public async Task Database_rejects_a_second_active_workout()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);

        var second = ActiveWorkout(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var operation = OutboxOperation.Create(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            second.Id,
            OutboxOperationType.StartWorkout,
            new StartWorkoutOutboxPayload(second.Id, second.StartedAt, []),
            second.BaseVersion,
            Now);

        await Assert.ThrowsAsync<SqliteException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(second, operation, default));
        Assert.Single(await fixture.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Stable_start_replay_returns_persisted_child_ids_and_rejects_contract_mismatches()
    {
        var workoutId = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
        var operationId = Guid.Parse("cccccccc-1111-2222-3333-dddddddddddd");
        var selections = new[] { new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted) };
        var fixture = CreateFixture();
        var first = await fixture.Coordinator.StartAsync(selections, workoutId, operationId);

        var recreated = CreateFixture();
        var replay = await recreated.Coordinator.StartAsync(selections, workoutId, operationId);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.Exercises.Select(item => item.Id), replay.Exercises.Select(item => item.Id));
        Assert.Single(await recreated.Outbox.PendingAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => recreated.Coordinator.StartAsync(
            [new WorkoutExerciseSelection(_exerciseId, TrackingMode.Bodyweight)],
            workoutId,
            operationId));
        await Assert.ThrowsAsync<InvalidDataException>(() => recreated.Coordinator.StartAsync(
            selections,
            workoutId,
            Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidDataException>(() => recreated.Coordinator.StartAsync(
            selections,
            Guid.NewGuid(),
            operationId));
        Assert.Single(await recreated.Outbox.PendingAsync());
    }

    [Fact]
    public async Task Corrupt_decimal_row_fails_closed_instead_of_returning_partial_workout()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(75m, null, 5));
        await ExecuteRawAsync("UPDATE LocalSet SET WeightKg = 'not-a-decimal';");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateFixture().Coordinator.RestoreActiveAsync());
    }

    [Fact]
    public async Task Set_operation_id_survives_restart_and_same_set_with_a_different_operation_fails_closed()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var saved = await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(75m, null, 5));

        var recreated = CreateFixture(Now.AddDays(1));
        var restoredSet = Assert.Single(Assert.Single(
            (await recreated.Coordinator.RestoreActiveAsync())!.Exercises).Sets);
        Assert.Equal(saved.OperationId, restoredSet.OperationId);
        var priorOperation = await recreated.Repository.GetOperationAsync(saved.OperationId, default);

        await recreated.Coordinator.SaveSetAsync(_exerciseId, saved);
        var differentOperation = saved with { OperationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            recreated.Coordinator.SaveSetAsync(_exerciseId, differentOperation));
        var afterReplay = Assert.Single(Assert.Single(
            (await recreated.Coordinator.RestoreActiveAsync())!.Exercises).Sets);
        Assert.Equal(saved.CompletedAt, afterReplay.CompletedAt);
        Assert.Equal(priorOperation,
            await recreated.Repository.GetOperationAsync(saved.OperationId, default));
        Assert.Equal(2, (await recreated.Outbox.PendingAsync()).Count);
    }

    [Fact]
    public async Task Session_reset_clears_workout_and_outbox_and_stale_queued_write_cannot_repopulate_them()
    {
        var database = new TrackZLocalDatabase(_databasePath);
        var realRepository = new LocalWorkoutRepository(database);
        var blocking = new BlockingWorkoutRepository(realRepository);
        var boundary = new AccountSessionBoundary();
        var coordinator = new ActiveWorkoutCoordinator(blocking, boundary, new FixedClock(Now));

        var stale = coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await blocking.Entered.Task;
        var reset = boundary.ResetAsync(coordinator.ClearPrivateDataAsync);
        blocking.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stale);
        await reset;
        Assert.Null(await new LocalWorkoutRepository(database).GetActiveAsync(default));
        Assert.Empty(await new OutboxRepository(database).PendingAsync());
    }

    [Fact]
    public async Task Restore_started_before_reset_never_returns_the_retired_session_workout()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var blocking = new BlockingWorkoutRepository(fixture.Repository) { BlockReads = true };
        var coordinator = new ActiveWorkoutCoordinator(blocking, fixture.Boundary, new FixedClock(Now));

        var restore = coordinator.RestoreActiveAsync();
        await blocking.Entered.Task;
        var reset = fixture.Boundary.ResetAsync(coordinator.ClearPrivateDataAsync);
        blocking.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
        await reset;
        Assert.Null(await fixture.Repository.GetActiveAsync(default));
    }

    [Fact]
    public async Task Active_graph_read_uses_one_snapshot_when_writer_commits_between_header_and_children()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var gate = new GatedWorkoutReadCheckpoint();
        var reader = new LocalWorkoutRepository(fixture.Database, gate);

        var snapshotRead = reader.GetActiveAsync(default);
        await gate.HeaderRead.Task;
        var writer = new ActiveWorkoutCoordinator(
            new LocalWorkoutRepository(new TrackZLocalDatabase(_databasePath)),
            new AccountSessionBoundary(),
            new FixedClock(Now));
        await writer.SaveSetAsync(_exerciseId, new LocalSet(80m, null, 6));
        gate.Release.TrySetResult();

        var snapshot = await snapshotRead;
        Assert.Empty(Assert.Single(snapshot!.Exercises).Sets);
        var latest = await fixture.Repository.GetActiveAsync(default);
        Assert.Single(Assert.Single(latest!.Exercises).Sets);
    }

    [Fact]
    public async Task Schema_initialization_is_idempotent_versioned_and_enforces_foreign_keys_after_restart()
    {
        var first = new TrackZLocalDatabase(_databasePath);
        await first.InitializeAsync();
        var second = new TrackZLocalDatabase(_databasePath);
        await second.InitializeAsync();

        await using var connection = await OpenRawAsync();
        await using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(TrackZLocalDatabase.CurrentSchemaVersion, Convert.ToInt32(await version.ExecuteScalarAsync()));

        await using var invalidChild = connection.CreateCommand();
        invalidChild.CommandText = """
            INSERT INTO LocalWorkoutExercise
                (Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder, DeletedAt, Version, BaseVersion)
            VALUES
                ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
                 'cccccccc-cccc-cccc-cccc-cccccccccccc', 1, 0, NULL, 1, 0);
            """;
        await Assert.ThrowsAsync<SqliteException>(() => invalidChild.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Schema_v1_migration_backfills_set_operation_id_from_its_save_set_outbox()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        var saved = await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(72.5m, null, 9));
        await DowngradeLocalSetToLegacyV1Async();

        var upgraded = new TrackZLocalDatabase(_databasePath);
        await upgraded.InitializeAsync();

        await using var connection = await OpenRawAsync();
        await using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(TrackZLocalDatabase.CurrentSchemaVersion, Convert.ToInt32(await version.ExecuteScalarAsync()));
        var restored = await new LocalWorkoutRepository(upgraded).GetActiveAsync(default);
        Assert.Equal(saved.OperationId, Assert.Single(Assert.Single(restored!.Exercises).Sets).OperationId);
    }

    [Fact]
    public async Task Pending_outbox_is_ordered_by_created_at_then_operation_id_and_retains_payload()
    {
        var fixture = CreateFixture();
        var workout = ActiveWorkout(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var laterId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var earlierId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var later = OutboxOperation.Create(
            laterId, workout.Id, OutboxOperationType.StartWorkout,
            new StartWorkoutOutboxPayload(workout.Id, workout.StartedAt, []), 0, Now);
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, later, default);
        var earlier = later with { OperationId = earlierId };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(workout, earlier, default);

        var pending = await fixture.Outbox.PendingAsync();
        Assert.Equal([earlierId, laterId], pending.Select(item => item.OperationId));
        Assert.All(pending, item => Assert.False(string.IsNullOrWhiteSpace(item.Payload)));
    }

    [Fact]
    public async Task Full_graph_order_persistence_supports_swap_middle_insert_and_delete_reindex()
    {
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var thirdId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var insertedId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var fixture = CreateFixture();
        var graph = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(secondId, TrackingMode.Bodyweight),
            new WorkoutExerciseSelection(thirdId, TrackingMode.Assisted)
        ]);

        var swapped = graph with
        {
            Version = graph.Version + 1,
            Exercises = [
                graph.Exercises[2] with { Order = 0, Version = graph.Exercises[2].Version + 1 },
                graph.Exercises[1],
                graph.Exercises[0] with { Order = 2, Version = graph.Exercises[0].Version + 1 }
            ]
        };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(
            swapped, ReorderOperation(swapped, graph.Version, 1), default);
        Assert.Equal(
            [thirdId, secondId, _exerciseId],
            (await fixture.Repository.GetActiveAsync(default))!.Exercises
                .Where(item => item.DeletedAt is null).OrderBy(item => item.Order)
                .Select(item => item.ExerciseDefinitionId));

        var inserted = new LocalWorkoutExercise(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            graph.Id,
            insertedId,
            TrackingMode.Weighted,
            1,
            null,
            1,
            0,
            []);
        var withMiddle = swapped with
        {
            Version = swapped.Version + 1,
            Exercises = [
                swapped.Exercises[0],
                inserted,
                swapped.Exercises[1] with { Order = 2, Version = swapped.Exercises[1].Version + 1 },
                swapped.Exercises[2] with { Order = 3, Version = swapped.Exercises[2].Version + 1 }
            ]
        };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(
            withMiddle, ReorderOperation(withMiddle, swapped.Version, 2), default);

        var deletedAt = Now.AddMinutes(3);
        var afterDelete = withMiddle with
        {
            Version = withMiddle.Version + 1,
            Exercises = [
                withMiddle.Exercises[0],
                withMiddle.Exercises[1] with { DeletedAt = deletedAt, Version = withMiddle.Exercises[1].Version + 1 },
                withMiddle.Exercises[2] with { Order = 1, Version = withMiddle.Exercises[2].Version + 1 },
                withMiddle.Exercises[3] with { Order = 2, Version = withMiddle.Exercises[3].Version + 1 }
            ]
        };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(
            afterDelete, ReorderOperation(afterDelete, withMiddle.Version, 3), default);

        var restored = await new LocalWorkoutRepository(
            new TrackZLocalDatabase(_databasePath)).GetActiveAsync(default);
        Assert.Equal(
            [thirdId, secondId, _exerciseId],
            restored!.Exercises.Where(item => item.DeletedAt is null).OrderBy(item => item.Order)
                .Select(item => item.ExerciseDefinitionId));
        Assert.Equal(deletedAt, restored.Exercises.Single(item => item.ExerciseDefinitionId == insertedId).DeletedAt);
    }

    [Fact]
    public async Task Reorder_outbox_failure_rolls_back_temporary_and_final_orders()
    {
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fixture = CreateFixture();
        var graph = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(secondId, TrackingMode.Bodyweight)
        ]);
        await ExecuteRawAsync("""
            CREATE TRIGGER FailReorderOutbox
            BEFORE INSERT ON OutboxOperation
            WHEN NEW.OperationType = 7
            BEGIN
                SELECT RAISE(ABORT, 'injected reorder outbox failure');
            END;
            """);
        var swapped = graph with
        {
            Version = graph.Version + 1,
            Exercises = [
                graph.Exercises[1] with { Order = 0, Version = graph.Exercises[1].Version + 1 },
                graph.Exercises[0] with { Order = 1, Version = graph.Exercises[0].Version + 1 }
            ]
        };

        var failure = await Assert.ThrowsAsync<SqliteException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(
                swapped, ReorderOperation(swapped, graph.Version, 1), default));
        Assert.Contains("injected reorder outbox failure", failure.Message, StringComparison.Ordinal);
        var restored = await fixture.Repository.GetActiveAsync(default);
        Assert.Equal(
            [_exerciseId, secondId],
            restored!.Exercises.OrderBy(item => item.Order).Select(item => item.ExerciseDefinitionId));
        Assert.Equal(graph.Version, restored.Version);
    }

    [Fact]
    public async Task Full_graph_write_rejects_omitted_rows_and_same_version_reorders_without_mutation()
    {
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var fixture = CreateFixture();
        var graph = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(secondId, TrackingMode.Bodyweight)
        ]);
        var omitted = graph with
        {
            Version = graph.Version + 1,
            Exercises = [graph.Exercises[0]]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(
                omitted, ReorderOperation(omitted, graph.Version, 2), default));

        var sameVersionSwap = graph with
        {
            Version = graph.Version + 1,
            Exercises = [
                graph.Exercises[1] with { Order = 0 },
                graph.Exercises[0] with { Order = 1 }
            ]
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Repository.SaveWorkoutAndEnqueueAsync(
                sameVersionSwap, ReorderOperation(sameVersionSwap, graph.Version, 3), default));

        var restored = await fixture.Repository.GetActiveAsync(default);
        Assert.Equal(graph.Version, restored!.Version);
        Assert.Equal(
            [_exerciseId, secondId],
            restored.Exercises.OrderBy(item => item.Order).Select(item => item.ExerciseDefinitionId));
        Assert.Single(await fixture.Outbox.PendingAsync(default));
    }

    [Fact]
    public async Task Full_graph_set_order_supports_swap_middle_insert_and_delete_reindex()
    {
        var fixture = CreateFixture();
        await fixture.Coordinator.StartAsync([new WorkoutExerciseSelection(_exerciseId, TrackingMode.Weighted)]);
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(50m, null, 12));
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(60m, null, 10));
        await fixture.Coordinator.SaveSetAsync(_exerciseId, new LocalSet(70m, null, 8));
        var graph = (await fixture.Repository.GetActiveAsync(default))!;
        var exercise = Assert.Single(graph.Exercises);
        var swappedExercise = exercise with
        {
            Version = exercise.Version + 1,
            Sets = [
                exercise.Sets[2] with { Order = 0, Version = exercise.Sets[2].Version + 1 },
                exercise.Sets[1],
                exercise.Sets[0] with { Order = 2, Version = exercise.Sets[0].Version + 1 }
            ]
        };
        var swapped = graph with
        {
            Version = graph.Version + 1,
            Exercises = [swappedExercise]
        };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(
            swapped, ReorderOperation(swapped, graph.Version, 4), default);

        var insertedSet = new LocalSet(
            Guid.NewGuid(),
            exercise.Id,
            1,
            55m,
            null,
            11,
            exercise.Sets.Max(item => item.CompletedAt).AddTicks(1),
            null,
            null,
            1,
            0,
            Guid.NewGuid());
        var middleExercise = swappedExercise with
        {
            Version = swappedExercise.Version + 1,
            Sets = [
                swappedExercise.Sets[0],
                insertedSet,
                swappedExercise.Sets[1] with { Order = 2, Version = swappedExercise.Sets[1].Version + 1 },
                swappedExercise.Sets[2] with { Order = 3, Version = swappedExercise.Sets[2].Version + 1 }
            ]
        };
        var withMiddle = swapped with
        {
            Version = swapped.Version + 1,
            Exercises = [middleExercise]
        };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(
            withMiddle, ReorderOperation(withMiddle, swapped.Version, 5), default);

        var deletedAt = Now.AddMinutes(6);
        var deletedExercise = middleExercise with
        {
            Version = middleExercise.Version + 1,
            Sets = [
                middleExercise.Sets[0],
                middleExercise.Sets[1] with { DeletedAt = deletedAt, Version = middleExercise.Sets[1].Version + 1 },
                middleExercise.Sets[2] with { Order = 1, Version = middleExercise.Sets[2].Version + 1 },
                middleExercise.Sets[3] with { Order = 2, Version = middleExercise.Sets[3].Version + 1 }
            ]
        };
        var afterDelete = withMiddle with
        {
            Version = withMiddle.Version + 1,
            Exercises = [deletedExercise]
        };
        await fixture.Repository.SaveWorkoutAndEnqueueAsync(
            afterDelete, ReorderOperation(afterDelete, withMiddle.Version, 6), default);

        var restored = await fixture.Repository.GetActiveAsync(default);
        var restoredSets = Assert.Single(restored!.Exercises).Sets;
        Assert.Equal([70m, 60m, 50m],
            restoredSets.Where(item => item.DeletedAt is null).OrderBy(item => item.Order)
                .Select(item => item.WeightKg!.Value));
        Assert.Equal(deletedAt, restoredSets.Single(item => item.Id == insertedSet.Id).DeletedAt);
    }

    private Fixture CreateFixture(DateTimeOffset? clockNow = null)
    {
        var database = new TrackZLocalDatabase(_databasePath);
        var repository = new LocalWorkoutRepository(database);
        var boundary = new AccountSessionBoundary();
        return new Fixture(
            database,
            repository,
            new OutboxRepository(database),
            boundary,
            new ActiveWorkoutCoordinator(repository, boundary, new FixedClock(clockNow ?? Now)));
    }

    private static LocalWorkout ActiveWorkout(Guid workoutId, Guid workoutExerciseId) => new(
        workoutId,
        LocalWorkoutStatus.Active,
        Now,
        null,
        null,
        1,
        0,
        [new LocalWorkoutExercise(
            workoutExerciseId,
            workoutId,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TrackingMode.Weighted,
            0,
            null,
            1,
            0,
            [])]);

    private static OutboxOperation ReorderOperation(LocalWorkout workout, long baseVersion, int minute) =>
        OutboxOperation.Create(
            Guid.NewGuid(),
            workout.Id,
            OutboxOperationType.ReorderExercises,
            new
            {
                workoutId = workout.Id,
                order = workout.Exercises.Where(item => item.DeletedAt is null)
                    .OrderBy(item => item.Order).Select(item => item.Id).ToArray()
            },
            baseVersion,
            Now.AddMinutes(minute));

    private async Task CreateOutboxFailureTriggerAsync() => await ExecuteRawAsync("""
        CREATE TRIGGER FailSaveSetOutbox
        BEFORE INSERT ON OutboxOperation
        WHEN NEW.OperationType = 2
        BEGIN
            SELECT RAISE(ABORT, 'injected outbox failure');
        END;
        """);

    private async Task ExecuteRawAsync(string sql)
    {
        await using var connection = await OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task DowngradeLocalSetToLegacyV1Async()
    {
        await using var connection = await OpenRawAsync();
        var hasOperationId = false;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(LocalSet);";
            await using var reader = await inspect.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                hasOperationId |= string.Equals(reader.GetString(1), "OperationId", StringComparison.Ordinal);
        }
        if (hasOperationId)
        {
            await using var downgrade = connection.CreateCommand();
            downgrade.CommandText = """
                DROP INDEX IF EXISTS UX_LocalSet_ActiveOrder;
                ALTER TABLE LocalSet RENAME TO LocalSetWithOperation;
                CREATE TABLE LocalSet (
                    Id TEXT PRIMARY KEY NOT NULL,
                    WorkoutExerciseId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL CHECK (SortOrder >= 0),
                    WeightKg TEXT NULL CHECK (WeightKg IS NULL OR typeof(WeightKg) = 'text'),
                    AssistedKg TEXT NULL CHECK (AssistedKg IS NULL OR typeof(AssistedKg) = 'text'),
                    Reps INTEGER NOT NULL CHECK (Reps BETWEEN 1 AND 999),
                    CompletedAt TEXT NOT NULL,
                    UpdatedAt TEXT NULL,
                    DeletedAt TEXT NULL,
                    Version INTEGER NOT NULL CHECK (Version >= 0),
                    BaseVersion INTEGER NOT NULL CHECK (BaseVersion >= 0 AND BaseVersion <= Version),
                    FOREIGN KEY (WorkoutExerciseId) REFERENCES LocalWorkoutExercise(Id) ON DELETE CASCADE,
                    CHECK (NOT (WeightKg IS NOT NULL AND AssistedKg IS NOT NULL))
                );
                INSERT INTO LocalSet
                    (Id, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                     CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
                SELECT Id, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg, Reps,
                       CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion
                FROM LocalSetWithOperation;
                DROP TABLE LocalSetWithOperation;
                CREATE UNIQUE INDEX UX_LocalSet_ActiveOrder
                    ON LocalSet(WorkoutExerciseId, SortOrder) WHERE DeletedAt IS NULL;
                """;
            await downgrade.ExecuteNonQueryAsync();
        }
        await using var markLegacy = connection.CreateCommand();
        markLegacy.CommandText = "PRAGMA user_version = 1;";
        await markLegacy.ExecuteNonQueryAsync();
    }

    private async Task<SqliteConnection> OpenRawAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await foreignKeys.ExecuteNonQueryAsync();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed record Fixture(
        TrackZLocalDatabase Database,
        LocalWorkoutRepository Repository,
        OutboxRepository Outbox,
        AccountSessionBoundary Boundary,
        ActiveWorkoutCoordinator Coordinator);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class GatedWorkoutReadCheckpoint : ILocalWorkoutReadCheckpoint
    {
        public TaskCompletionSource HeaderRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AfterHeaderReadAsync(CancellationToken cancellationToken)
        {
            HeaderRead.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingWorkoutRepository(ILocalWorkoutRepository inner) : ILocalWorkoutRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockReads { get; init; }

        public async Task SaveWorkoutAndEnqueueAsync(
            LocalWorkout workout,
            OutboxOperation operation,
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            await inner.SaveWorkoutAndEnqueueAsync(workout, operation, cancellationToken);
        }

        public async Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken)
        {
            if (BlockReads)
            {
                Entered.TrySetResult();
                await Release.Task;
            }
            return await inner.GetActiveAsync(cancellationToken);
        }

        public Task<OutboxOperation?> GetOperationAsync(
            Guid operationId,
            CancellationToken cancellationToken) =>
            inner.GetOperationAsync(operationId, cancellationToken);

        public Task<DateTimeOffset?> GetLatestOperationCreatedAtAsync(
            CancellationToken cancellationToken) =>
            inner.GetLatestOperationCreatedAtAsync(cancellationToken);

        public Task ClearPrivateDataAsync(CancellationToken cancellationToken) =>
            inner.ClearPrivateDataAsync(cancellationToken);
    }
}
