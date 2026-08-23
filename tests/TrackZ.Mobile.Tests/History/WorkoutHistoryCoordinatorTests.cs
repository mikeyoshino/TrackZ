using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.History;

public sealed class WorkoutHistoryCoordinatorTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"trackz-history-{Guid.NewGuid():N}.db");
    private readonly MutableClock _clock = new(
        new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero));
    private readonly AccountSessionBoundary _boundary = new();

    [Fact]
    public async Task Rated_set_round_trips_through_repository_edit_and_undo_snapshot()
    {
        var repository = new LocalWorkoutRepository(Database());
        var workoutId = Guid.NewGuid();
        var workoutExerciseId = Guid.NewGuid();
        var exerciseDefinitionId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var saveOperationId = Guid.NewGuid();
        var editOperationId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 8, 23, 8, 0, 0, TimeSpan.Zero);
        var setCompletedAt = startedAt.AddMinutes(1);
        var completedAt = startedAt.AddMinutes(2);
        var editedAt = startedAt.AddMinutes(3);
        var rated = new LocalSet(
            setId, workoutExerciseId, 0, 70m, null, 10,
            setCompletedAt, setCompletedAt.AddTicks(1), null,
            2, 1, saveOperationId, SetEffortRating.Easy);
        var previous = new LocalWorkout(
            workoutId, LocalWorkoutStatus.Completed, startedAt, completedAt,
            null, 3, 0,
            [new LocalWorkoutExercise(
                workoutExerciseId, workoutId, exerciseDefinitionId,
                TrackingMode.Weighted, 0, null, 2, 0, [rated])]);
        var seedOperation = OutboxOperation.Create(
            saveOperationId,
            workoutId,
            OutboxOperationType.SaveSet,
            new SaveSetOutboxPayload(
                workoutId, workoutExerciseId, setId, 0,
                "70", null, 10, setCompletedAt),
            2,
            setCompletedAt);
        await repository.SaveWorkoutAndEnqueueAsync(previous, seedOperation);

        var editedSet = rated with
        {
            WeightKg = 72.5m,
            Reps = 9,
            UpdatedAt = editedAt,
            Version = rated.Version + 1
        };
        var edited = previous with
        {
            Version = previous.Version + 1,
            Exercises = [previous.Exercises[0] with
            {
                Version = previous.Exercises[0].Version + 1,
                Sets = [editedSet]
            }]
        };
        var editOperation = OutboxOperation.Create(
            editOperationId,
            workoutId,
            OutboxOperationType.EditSet,
            new EditSetOutboxPayload(
                workoutId, workoutExerciseId, setId,
                "72.5", null, 9, editedAt),
            previous.Version,
            editedAt);
        await repository.SaveHistoryMutationAndEnqueueAsync(
            previous, edited, editOperation);

        var reloaded = Assert.Single(await repository.GetHistoryAsync());
        var reloadedSet = Assert.Single(Assert.Single(reloaded.Exercises).Sets);
        Assert.Equal(SetEffortRating.Easy, reloadedSet.Effort);
        Assert.Equal(72.5m, reloadedSet.WeightKg);
        Assert.Equal(9, reloadedSet.Reps);

        var restored = await repository.UndoHistoryMutationAsync(
            editOperationId, editedAt.AddMinutes(1));
        var restoredSet = Assert.Single(Assert.Single(restored.Exercises).Sets);
        Assert.Equal(SetEffortRating.Easy, restoredSet.Effort);
        Assert.Equal(70m, restoredSet.WeightKg);
        Assert.Equal(10, restoredSet.Reps);
    }

    [Fact]
    public async Task Completion_persists_undo_before_mutation_and_can_restore_active_workout_before_send()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        var started = await active.StartAsync(
            [new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var operationId = Guid.NewGuid();

        var completed = await active.FinishAsync(operationId);

        Assert.Equal(LocalWorkoutStatus.Completed, completed.Status);
        Assert.Equal(1, await UndoCountAsync());
        var restored = await History().UndoAsync(operationId);
        Assert.Equal(LocalWorkoutStatus.Active, restored.Status);
        Assert.Null(restored.CompletedAt);
        Assert.Equal(started.Id, (await Coordinator().RestoreActiveAsync())!.Id);
        Assert.Empty(await History().GetHistoryAsync());
        Assert.DoesNotContain(await Outbox().PendingAsync(), item => item.OperationId == operationId);
        Assert.Equal(0, await UndoCountAsync());
    }

    [Fact]
    public async Task Completion_replay_with_same_operation_id_returns_exact_durable_graph()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync(
            [new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var operationId = Guid.NewGuid();
        var completed = await active.FinishAsync(operationId);

        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        var replay = await Coordinator().FinishAsync(operationId);

        Assert.Equal(completed.Id, replay.Id);
        Assert.Equal(completed.Status, replay.Status);
        Assert.Equal(completed.CompletedAt, replay.CompletedAt);
        Assert.Equal(
            Assert.Single(completed.Exercises).Sets.Select(set => (set.Id, set.Reps)),
            Assert.Single(replay.Exercises).Sets.Select(set => (set.Id, set.Reps)));
        Assert.Single(await Outbox().PendingAsync(), item => item.OperationId == operationId);
        Assert.Equal(1, await UndoCountAsync());
    }

    [Fact]
    public async Task Offline_edit_delete_restart_and_undo_restore_exact_graph_and_neutralize_intent()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var first = await active.SaveSetAsync(exerciseId, new LocalSet(70m, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var second = await active.SaveSetAsync(exerciseId, new LocalSet(72.5m, null, 8));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        var exercise = Assert.Single(completed.Exercises);

        var editId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var edit = await History().EditSetAsync(
            completed.Id,
            exercise.Id,
            first.Id,
            new HistorySetMeasurement(75.125m, null, 6),
            editId);
        var deleteId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var deleted = await History().DeleteSetAsync(
            completed.Id, exercise.Id, second.Id, deleteId);

        var restartedHistory = History();
        var restored = Assert.Single(await restartedHistory.GetHistoryAsync());
        var restoredSets = Assert.Single(restored.Exercises).Sets;
        Assert.Equal(75.125m, restoredSets.Single(set => set.Id == first.Id).WeightKg);
        Assert.Equal(6, restoredSets.Single(set => set.Id == first.Id).Reps);
        Assert.NotNull(restoredSets.Single(set => set.Id == second.Id).DeletedAt);
        Assert.Equal(0, restoredSets.Single(set => set.Id == first.Id).Order);

        var historyOperations = (await Outbox().PendingAsync())
            .Where(operation => operation.Type is OutboxOperationType.EditSet or OutboxOperationType.DeleteSet)
            .ToArray();
        Assert.Equal([editId, deleteId], historyOperations.Select(operation => operation.OperationId));
        Assert.Equal([4L, 5L], historyOperations.Select(operation => operation.BaseVersion));
        Assert.True(historyOperations[0].CreatedAt < historyOperations[1].CreatedAt);
        Assert.Equal(edit.Workout.Version + 1, deleted.Workout.Version);

        await restartedHistory.UndoAsync(deleteId);
        var afterDeleteUndo = Assert.Single(await restartedHistory.GetHistoryAsync());
        Assert.Null(Assert.Single(afterDeleteUndo.Exercises).Sets.Single(set => set.Id == second.Id).DeletedAt);
        await restartedHistory.UndoAsync(editId);
        var exactOriginal = Assert.Single(await History().GetHistoryAsync());
        Assert.Equal([70m, 72.5m], Assert.Single(exactOriginal.Exercises).Sets
            .OrderBy(set => set.Order).Select(set => set.WeightKg!.Value));
        Assert.Equal([10, 8], Assert.Single(exactOriginal.Exercises).Sets
            .OrderBy(set => set.Order).Select(set => set.Reps));
        Assert.DoesNotContain(await Outbox().PendingAsync(), operation =>
            operation.OperationId == editId || operation.OperationId == deleteId);
        Assert.Equal(1, await UndoCountAsync());
    }

    [Fact]
    public async Task Delete_workout_exercise_queues_typed_intent_and_undo_restores_exact_snapshot()
    {
        var firstDefinitionId = Guid.NewGuid();
        var secondDefinitionId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([
            new WorkoutExerciseSelection(firstDefinitionId, TrackingMode.Bodyweight),
            new WorkoutExerciseSelection(secondDefinitionId, TrackingMode.Weighted)
        ]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await active.SaveSetAsync(firstDefinitionId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        await active.SaveSetAsync(secondDefinitionId, new LocalSet(70.125m, null, 8));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        var removed = completed.Exercises.Single(item =>
            item.ExerciseDefinitionId == firstDefinitionId);
        var remaining = completed.Exercises.Single(item =>
            item.ExerciseDefinitionId == secondDefinitionId);
        var operationId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);

        var mutation = await History().DeleteWorkoutExerciseAsync(
            completed.Id, removed.Id, operationId);

        var operation = Assert.Single(
            await Outbox().PendingAsync(),
            item => item.OperationId == operationId);
        var payload = operation.DeserializePayload<DeleteWorkoutExerciseOutboxPayload>();
        Assert.Equal(OutboxOperationType.DeleteWorkoutExercise, operation.Type);
        Assert.Equal(completed.Version, operation.BaseVersion);
        Assert.Equal(completed.Id, payload.WorkoutId);
        Assert.Equal(removed.Id, payload.WorkoutExerciseId);
        Assert.Equal(operation.CreatedAt, payload.DeletedAt);
        Assert.NotNull(mutation.Workout.Exercises.Single(item => item.Id == removed.Id).DeletedAt);
        Assert.Equal(0, mutation.Workout.Exercises.Single(item => item.Id == remaining.Id).Order);
        Assert.Equal(2, await UndoCountAsync());

        var restored = await History().UndoAsync(operationId);

        Assert.Null(restored.Exercises.Single(item => item.Id == removed.Id).DeletedAt);
        Assert.Equal([0, 1], restored.Exercises.OrderBy(item => item.Order).Select(item => item.Order));
        Assert.Equal(70.125m, Assert.Single(restored.Exercises
            .Single(item => item.Id == remaining.Id).Sets).WeightKg);
        Assert.DoesNotContain(await Outbox().PendingAsync(), item => item.OperationId == operationId);
        Assert.Equal(1, await UndoCountAsync());
    }

    [Fact]
    public async Task Undo_fails_closed_after_send_starts_and_snapshot_purges_only_after_applied_pull()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        var exercise = Assert.Single(completed.Exercises);
        var initialApi = new AppliedApi(CompletedGraph(completed));
        await new SyncCoordinator(Database(), initialApi, _boundary, _clock).RunOnceAsync();

        var editOperationId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var edited = await History().EditSetAsync(
            completed.Id,
            exercise.Id,
            set.Id,
            new HistorySetMeasurement(null, null, 12),
            editOperationId);
        Assert.Equal(1, await UndoCountAsync());

        var api = new GatedAppliedApi(CompletedGraph(edited.Workout));
        var syncTask = new SyncCoordinator(Database(), api, _boundary, _clock).RunOnceAsync();
        await api.PushStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => History().UndoAsync(editOperationId));
        Assert.Equal(1, await UndoCountAsync());

        api.ReleasePush.TrySetResult();
        await api.PullStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => History().UndoAsync(editOperationId));
        Assert.Equal(1, await UndoCountAsync());

        api.ReleasePull.TrySetResult();
        await syncTask;
        Assert.Equal(0, await UndoCountAsync());
        var authoritative = Assert.Single(await History().GetHistoryAsync());
        Assert.Equal(12, Assert.Single(Assert.Single(authoritative.Exercises).Sets).Reps);
    }

    [Fact]
    public async Task Mode_validation_rejects_noncanonical_history_edit_without_graph_or_outbox_write()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Assisted)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(null, 25m, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        var pendingBefore = await Outbox().PendingAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => History().EditSetAsync(
            completed.Id,
            Assert.Single(completed.Exercises).Id,
            set.Id,
            new HistorySetMeasurement(70m, null, 8),
            Guid.NewGuid()));

        Assert.Equal(pendingBefore, await Outbox().PendingAsync());
        var unchanged = Assert.Single(await History().GetHistoryAsync());
        Assert.Equal(25m, Assert.Single(Assert.Single(unchanged.Exercises).Sets).AssistedKg);
    }

    [Fact]
    public async Task Exact_no_op_edit_does_not_advance_graph_outbox_or_undo_and_real_successor_syncs()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(70m, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        await new SyncCoordinator(Database(), new AppliedApi(CompletedGraph(completed)), _boundary, _clock)
            .RunOnceAsync();
        var exercise = Assert.Single(completed.Exercises);
        var beforeUndo = await UndoCountAsync();
        var noOpId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddHours(1);

        var noOp = await History().EditSetAsync(
            completed.Id,
            exercise.Id,
            set.Id,
            new HistorySetMeasurement(70m, null, 10),
            noOpId);

        Assert.Equal(Guid.Empty, noOp.OperationId);
        Assert.Equal(completed.Version, noOp.Workout.Version);
        Assert.Equal(set.UpdatedAt, Assert.Single(Assert.Single(noOp.Workout.Exercises).Sets).UpdatedAt);
        Assert.Null(await new LocalWorkoutRepository(Database()).GetOperationAsync(noOpId));
        Assert.Equal(beforeUndo, await UndoCountAsync());

        var changed = await History().EditSetAsync(
            completed.Id,
            exercise.Id,
            set.Id,
            new HistorySetMeasurement(72.5m, null, 8),
            noOpId);
        var operation = await new LocalWorkoutRepository(Database()).GetOperationAsync(noOpId);
        Assert.Equal(noOpId, changed.OperationId);
        Assert.NotNull(operation);
        Assert.Equal(completed.Version, operation.BaseVersion);
        Assert.Equal(completed.Version + 1, changed.Workout.Version);

        var api = new QueueSyncApi();
        api.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(noOpId, SyncOperationStatus.Applied, changed.Workout.Version, null)
        ]));
        api.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(
                2, "Workout", changed.Workout.Id, changed.Workout.Version, false,
                changed.Workout.CompletedAt!.Value, CompletedGraph(changed.Workout))
        ], "cursor-2", false));
        await new SyncCoordinator(Database(), api, _boundary, _clock).RunOnceAsync();
        Assert.Empty(await Outbox().PendingAsync());
    }

    [Fact]
    public async Task Tombstone_pull_to_second_cache_hides_acknowledged_set_from_history()
    {
        var workoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var activeSetId = Guid.NewGuid();
        var deletedSetId = Guid.NewGuid();
        var startedAt = _clock.UtcNow;
        var deletedAt = startedAt.AddMinutes(4);
        var graph = new SyncWorkoutDto(
            workoutId,
            (int)LocalWorkoutStatus.Completed,
            startedAt,
            startedAt.AddMinutes(3),
            null,
            5,
            [new SyncWorkoutExerciseDto(
                exerciseId,
                Guid.NewGuid(),
                (int)TrackingMode.Bodyweight,
                0,
                null,
                3,
                [
                    new SyncSetDto(
                        activeSetId, 0, null, null, 10,
                        startedAt.AddMinutes(1), null, null, 1),
                    new SyncSetDto(
                        deletedSetId, 1, null, null, 8,
                        startedAt.AddMinutes(2), null, deletedAt, 2)
                ])]);
        var api = new QueueSyncApi();
        api.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(1, "Workout", workoutId, 5, false, deletedAt, graph)
        ], "cursor-1", false));

        await new SyncCoordinator(Database(), api, _boundary, _clock).RunOnceAsync();

        var history = Assert.Single(await History().GetHistoryAsync());
        var visible = Assert.Single(history.Exercises).Sets;
        Assert.Equal(activeSetId, Assert.Single(visible).Id);
        Assert.DoesNotContain(visible, set => set.Id == deletedSetId);
    }

    [Fact]
    public async Task Two_device_edit_conflict_retains_payload_and_apply_local_rebases_without_loss()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        var initialApi = new AppliedApi(CompletedGraph(completed));
        await new SyncCoordinator(Database(), initialApi, _boundary, _clock).RunOnceAsync();

        var editOperationId = Guid.NewGuid();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var localEdit = await History().EditSetAsync(
            completed.Id,
            Assert.Single(completed.Exercises).Id,
            set.Id,
            new HistorySetMeasurement(null, null, 12),
            editOperationId);
        var original = Assert.Single(await Outbox().PendingAsync());
        var remoteUpdatedAt = _clock.UtcNow.AddMinutes(1);
        var remote = EditedGraph(completed, 8, 4, remoteUpdatedAt);
        var conflictApi = new QueueSyncApi();
        conflictApi.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                editOperationId, SyncOperationStatus.Conflict, 4,
                TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict)
        ]));
        conflictApi.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(2, "Workout", remote.Id, 4, false, remoteUpdatedAt, remote)
        ], "cursor-2", false));
        var sync = new SyncCoordinator(Database(), conflictApi, _boundary, _clock);
        await sync.RunOnceAsync();

        var conflicted = Assert.Single(await Outbox().ConflictedAsync());
        Assert.Equal(original.Payload, conflicted.Payload);
        Assert.Equal(1, await UndoCountAsync());
        var replacement = await new ConflictResolution(sync)
            .ApplyLocalAgainstVersionAsync(editOperationId, 4);
        var rebased = replacement.DeserializePayload<EditSetOutboxPayload>();
        Assert.Equal(OutboxOperationType.EditSet, replacement.Type);
        Assert.Equal(4, replacement.BaseVersion);
        Assert.Equal(12, rebased.Reps);
        Assert.True(rebased.UpdatedAt > remoteUpdatedAt);
        Assert.Equal(editOperationId, replacement.ReplacesOperationId);

        var applied = EditedGraph(completed, 12, 5, rebased.UpdatedAt);
        conflictApi.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                replacement.OperationId, SyncOperationStatus.Applied, 5, null)
        ]));
        conflictApi.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(3, "Workout", applied.Id, 5, false, rebased.UpdatedAt, applied)
        ], "cursor-3", false));
        await sync.RunOnceAsync();

        var final = Assert.Single(await History().GetHistoryAsync());
        Assert.Equal(12, Assert.Single(Assert.Single(final.Exercises).Sets).Reps);
        Assert.Empty(await Outbox().PendingAsync());
        Assert.Empty(await Outbox().ConflictedAsync());
        Assert.Equal(0, await UndoCountAsync());
        Assert.Equal(localEdit.Workout.Id, final.Id);
    }

    [Fact]
    public async Task Apply_local_transfers_durable_undo_and_restart_undo_neutralizes_replacement_chain()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        await new SyncCoordinator(Database(), new AppliedApi(CompletedGraph(completed)), _boundary, _clock)
            .RunOnceAsync();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var originalId = Guid.NewGuid();
        await History().EditSetAsync(
            completed.Id,
            Assert.Single(completed.Exercises).Id,
            set.Id,
            new HistorySetMeasurement(null, null, 12),
            originalId);
        var remoteAt = _clock.UtcNow.AddMinutes(1);
        var remote = EditedGraph(completed, 8, 4, remoteAt);
        var conflictApi = new QueueSyncApi();
        conflictApi.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                originalId, SyncOperationStatus.Conflict, 4,
                TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict)
        ]));
        conflictApi.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(2, "Workout", remote.Id, 4, false, remoteAt, remote)
        ], "cursor-2", false));
        var sync = new SyncCoordinator(Database(), conflictApi, _boundary, _clock);
        await sync.RunOnceAsync();
        var replacement = await new ConflictResolution(sync)
            .ApplyLocalAgainstVersionAsync(originalId, 4);

        var restored = await new WorkoutHistoryCoordinator(
                new LocalWorkoutRepository(new TrackZLocalDatabase(_path)), _boundary, _clock)
            .UndoAsync(replacement.OperationId);

        Assert.Equal(10, Assert.Single(Assert.Single(restored.Exercises).Sets).Reps);
        Assert.Empty(await Outbox().PendingAsync());
        Assert.Empty(await Outbox().ConflictedAsync());
        Assert.Equal(0, await UndoCountAsync());
        Assert.NotNull((await new LocalWorkoutRepository(Database())
            .GetOperationAsync(originalId))!.NeutralizedAt);
        Assert.NotNull((await new LocalWorkoutRepository(Database())
            .GetOperationAsync(replacement.OperationId))!.NeutralizedAt);
    }

    [Fact]
    public async Task Two_device_edit_conflict_keep_server_applies_authority_and_cleans_undo()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        await new SyncCoordinator(Database(), new AppliedApi(CompletedGraph(completed)), _boundary, _clock)
            .RunOnceAsync();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var editId = Guid.NewGuid();
        await History().EditSetAsync(
            completed.Id, Assert.Single(completed.Exercises).Id, set.Id,
            new HistorySetMeasurement(null, null, 12), editId);
        var remoteAt = _clock.UtcNow.AddMinutes(1);
        var remote = EditedGraph(completed, 8, 4, remoteAt);
        var api = new QueueSyncApi();
        api.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                editId, SyncOperationStatus.Conflict, 4,
                TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict)
        ]));
        api.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(2, "Workout", remote.Id, 4, false, remoteAt, remote)
        ], "cursor-2", false));
        var sync = new SyncCoordinator(Database(), api, _boundary, _clock);
        await sync.RunOnceAsync();
        var replacement = await new ConflictResolution(sync)
            .ApplyLocalAgainstVersionAsync(editId, 4);
        var secondRemoteAt = remoteAt.AddMinutes(1);
        var secondRemote = EditedGraph(completed, 7, 5, secondRemoteAt);
        api.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                replacement.OperationId, SyncOperationStatus.Conflict, 5,
                TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict)
        ]));
        api.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(
                3, "Workout", secondRemote.Id, 5, false, secondRemoteAt, secondRemote)
        ], "cursor-3", false));
        await sync.RunOnceAsync();

        await new ConflictResolution(sync).KeepServerAsync(replacement.OperationId);

        var kept = Assert.Single(await History().GetHistoryAsync());
        Assert.Equal(7, Assert.Single(Assert.Single(kept.Exercises).Sets).Reps);
        Assert.Empty(await Outbox().PendingAsync());
        Assert.Empty(await Outbox().ConflictedAsync());
        Assert.Empty(await Outbox().ForHistoryWorkoutAsync(completed.Id));
        Assert.Equal(0, await UndoCountAsync());
        Assert.NotNull((await new LocalWorkoutRepository(Database())
            .GetOperationAsync(editId))!.NeutralizedAt);
        Assert.NotNull((await new LocalWorkoutRepository(Database())
            .GetOperationAsync(replacement.OperationId))!.NeutralizedAt);
    }

    [Fact]
    public async Task Sent_replacement_blocks_keep_server_and_undo_until_retryable_result_proves_not_committed()
    {
        var exerciseId = Guid.NewGuid();
        var active = Coordinator();
        await active.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)]);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var set = await active.SaveSetAsync(exerciseId, new LocalSet(null, null, 10));
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var completed = await active.FinishAsync();
        await new SyncCoordinator(Database(), new AppliedApi(CompletedGraph(completed)), _boundary, _clock)
            .RunOnceAsync();
        _clock.UtcNow = _clock.UtcNow.AddMinutes(1);
        var originalId = Guid.NewGuid();
        await History().EditSetAsync(
            completed.Id,
            Assert.Single(completed.Exercises).Id,
            set.Id,
            new HistorySetMeasurement(null, null, 12),
            originalId);
        var remoteAt = _clock.UtcNow.AddMinutes(1);
        var remote = EditedGraph(completed, 8, 4, remoteAt);
        var conflictApi = new QueueSyncApi();
        conflictApi.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                originalId, SyncOperationStatus.Conflict, 4,
                TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict)
        ]));
        conflictApi.PullResponses.Enqueue(new SyncPullResponse([
            new SyncChangeDto(2, "Workout", remote.Id, 4, false, remoteAt, remote)
        ], "cursor-2", false));
        var sync = new SyncCoordinator(Database(), conflictApi, _boundary, _clock);
        await sync.RunOnceAsync();
        var replacement = await new ConflictResolution(sync)
            .ApplyLocalAgainstVersionAsync(originalId, 4);

        Assert.Equal(SyncRunStatus.Offline, await new SyncCoordinator(
            Database(), new ThrowingPushApi(), _boundary, _clock).RunOnceAsync());
        var restartedBoundary = new AccountSessionBoundary();
        var restartedDatabase = new TrackZLocalDatabase(_path);
        var restartedRepository = new LocalWorkoutRepository(restartedDatabase);
        var ambiguousOriginal = await restartedRepository.GetOperationAsync(originalId);
        var ambiguousReplacement = await restartedRepository.GetOperationAsync(replacement.OperationId);
        var ambiguousGraph = Assert.Single(await restartedRepository.GetHistoryAsync());
        var ambiguousSnapshot = await UndoSnapshotAsync(replacement.OperationId);
        Assert.NotNull(ambiguousReplacement!.SendStartedAt);
        Assert.NotNull(ambiguousSnapshot);

        var restartedSync = new SyncCoordinator(
            restartedDatabase, new ThrowingPushApi(), restartedBoundary, _clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ConflictResolution(restartedSync).KeepServerAsync(originalId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkoutHistoryCoordinator(restartedRepository, restartedBoundary, _clock)
                .UndoAsync(replacement.OperationId));

        Assert.Equal(ambiguousOriginal, await restartedRepository.GetOperationAsync(originalId));
        Assert.Equal(ambiguousReplacement, await restartedRepository.GetOperationAsync(replacement.OperationId));
        Assert.Equal(ambiguousSnapshot, await UndoSnapshotAsync(replacement.OperationId));
        Assert.Equal(
            JsonSerializer.Serialize(ambiguousGraph),
            JsonSerializer.Serialize(Assert.Single(await restartedRepository.GetHistoryAsync())));

        var retryableApi = new QueueSyncApi();
        retryableApi.PushResponses.Enqueue(new SyncPushResponse([
            new SyncOperationResultDto(
                replacement.OperationId,
                SyncOperationStatus.Retryable,
                null,
                TrackZ.Contracts.Errors.BusinessErrorCode.InternalServerError)
        ]));
        Assert.Equal(SyncRunStatus.Completed, await new SyncCoordinator(
            restartedDatabase, retryableApi, restartedBoundary, _clock).RunOnceAsync());
        var retryable = await restartedRepository.GetOperationAsync(replacement.OperationId);
        Assert.Equal(OutboxOperationState.Pending, retryable!.State);
        Assert.Null(retryable.SendStartedAt);
        Assert.Equal(1, retryable.RetryCount);

        await new ConflictResolution(new SyncCoordinator(
            restartedDatabase, retryableApi, restartedBoundary, _clock))
            .KeepServerAsync(originalId);
        var kept = Assert.Single(await restartedRepository.GetHistoryAsync());
        Assert.Equal(8, Assert.Single(Assert.Single(kept.Exercises).Sets).Reps);
        Assert.Empty(await new OutboxRepository(restartedDatabase)
            .ForHistoryWorkoutAsync(completed.Id));
        Assert.Equal(0, await UndoCountAsync());
    }

    private ActiveWorkoutCoordinator Coordinator() => new(
        new LocalWorkoutRepository(Database()), _boundary, _clock);

    private WorkoutHistoryCoordinator History() => new(
        new LocalWorkoutRepository(Database()), _boundary, _clock);

    private TrackZLocalDatabase Database() => new(_path);
    private OutboxRepository Outbox() => new(Database());

    private async Task<long> UndoCountAsync()
    {
        await Database().InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM HistoryUndo;";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string?> UndoSnapshotAsync(Guid operationId)
    {
        await Database().InitializeAsync();
        await using var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SnapshotJson FROM HistoryUndo WHERE OperationId = $id;";
        command.Parameters.AddWithValue("$id", operationId.ToString("D"));
        return (string?)await command.ExecuteScalarAsync();
    }

    private static SyncWorkoutDto CompletedGraph(LocalWorkout workout) => new(
        workout.Id, (int)workout.Status, workout.StartedAt, workout.CompletedAt,
        workout.DeletedAt, workout.Version,
        workout.Exercises.Select(exercise => new SyncWorkoutExerciseDto(
            exercise.Id, exercise.ExerciseDefinitionId, (int)exercise.TrackingMode,
            exercise.Order, exercise.DeletedAt, exercise.Version,
            exercise.Sets.Select(set => new SyncSetDto(
                set.Id, set.Order,
                set.WeightKg?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                set.AssistedKg?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                set.Reps, set.CompletedAt, set.UpdatedAt, set.DeletedAt, set.Version,
                set.Effort)).ToArray()
        )).ToArray());

    private static SyncWorkoutDto EditedGraph(
        LocalWorkout source,
        int reps,
        long version,
        DateTimeOffset updatedAt)
    {
        var graph = CompletedGraph(source);
        var exercise = Assert.Single(graph.Exercises);
        var set = Assert.Single(exercise.Sets);
        return graph with
        {
            Version = version,
            Exercises =
            [
                exercise with
                {
                    Version = exercise.Version + (version - source.Version),
                    Sets = [set with { Reps = reps, UpdatedAt = updatedAt, Version = set.Version + (version - source.Version) }]
                }
            ]
        };
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var path = _path + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
        return ValueTask.CompletedTask;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class AppliedApi(SyncWorkoutDto graph) : ISyncApi
    {
        private bool _pulled;

        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            var operation = Assert.Single(request.Operations);
            return Task.FromResult(new SyncPushResponse([
                new SyncOperationResultDto(
                    operation.OperationId, SyncOperationStatus.Applied,
                    operation.BaseVersion!.Value + 1, null)
            ]));
        }

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default)
        {
            if (_pulled) return Task.FromResult(new SyncPullResponse([], cursor, false));
            _pulled = true;
            return Task.FromResult(new SyncPullResponse([
                new SyncChangeDto(1, "Workout", graph.Id, graph.Version, false,
                    graph.CompletedAt!.Value, graph)
            ], "cursor-1", false));
        }
    }

    private sealed class GatedAppliedApi(SyncWorkoutDto graph) : ISyncApi
    {
        public TaskCompletionSource PushStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PullStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePull { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            var operation = Assert.Single(request.Operations);
            PushStarted.TrySetResult();
            await ReleasePush.Task.WaitAsync(cancellationToken);
            return new SyncPushResponse([
                new SyncOperationResultDto(
                    operation.OperationId, SyncOperationStatus.Applied, graph.Version, null)
            ]);
        }

        public async Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default)
        {
            PullStarted.TrySetResult();
            await ReleasePull.Task.WaitAsync(cancellationToken);
            return new SyncPullResponse([
                new SyncChangeDto(2, "Workout", graph.Id, graph.Version, false,
                    graph.CompletedAt!.Value, graph)
            ], "cursor-2", false);
        }
    }

    private sealed class QueueSyncApi : ISyncApi
    {
        public Queue<SyncPushResponse> PushResponses { get; } = [];
        public Queue<SyncPullResponse> PullResponses { get; } = [];

        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PushResponses.Dequeue());

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PullResponses.Count == 0
                ? new SyncPullResponse([], cursor, false)
                : PullResponses.Dequeue());
    }

    private sealed class ThrowingPushApi : ISyncApi
    {
        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("The push outcome is ambiguous.");

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Pull is not reached after an ambiguous push.");
    }
}
