using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
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

        await new ConflictResolution(sync).KeepServerAsync(editId);

        var kept = Assert.Single(await History().GetHistoryAsync());
        Assert.Equal(8, Assert.Single(Assert.Single(kept.Exercises).Sets).Reps);
        Assert.Empty(await Outbox().PendingAsync());
        Assert.Empty(await Outbox().ConflictedAsync());
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
                set.Reps, set.CompletedAt, set.UpdatedAt, set.DeletedAt, set.Version)).ToArray()
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
}
