using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Acceptance;

public sealed class OfflineWorkoutAcceptanceTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"trackz-offline-acceptance-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Offline_log_kill_restore_and_retry_sync_creates_each_set_once()
    {
        var exerciseId = Guid.NewGuid();
        var clock = new MutableClock(new DateTimeOffset(2026, 8, 16, 8, 0, 0, TimeSpan.Zero));
        var boundary = new AccountSessionBoundary();
        var firstDatabase = new TrackZLocalDatabase(_path);
        var firstRepository = new LocalWorkoutRepository(firstDatabase);
        var first = new ActiveWorkoutCoordinator(firstRepository, boundary, clock);
        await first.StartAsync([new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)]);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await first.SaveSetAsync(exerciseId, new LocalSet(70m, null, 10));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await first.SaveSetAsync(exerciseId, new LocalSet(70m, null, 9));

        // Simulate a hard process kill by discarding every coordinator/repository instance.
        var restoredDatabase = new TrackZLocalDatabase(_path);
        var restoredRepository = new LocalWorkoutRepository(restoredDatabase);
        var restoredCoordinator = new ActiveWorkoutCoordinator(restoredRepository, boundary, clock);
        var restoredActive = await restoredCoordinator.RestoreActiveAsync();
        Assert.Equal(2, Assert.Single(restoredActive!.Exercises).Sets.Count);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var completed = await restoredCoordinator.FinishAsync();

        var historyAfterSecondRestart = await new LocalWorkoutRepository(
            new TrackZLocalDatabase(_path)).GetHistoryAsync();
        var historyWorkout = Assert.Single(historyAfterSecondRestart);
        Assert.Equal(LocalWorkoutStatus.Completed, historyWorkout.Status);
        Assert.Equal([10, 9], Assert.Single(historyWorkout.Exercises).Sets.Select(set => set.Reps));

        var outbox = new OutboxRepository(restoredDatabase);
        var operations = await outbox.PendingAsync();
        Assert.Equal(
            [OutboxOperationType.StartWorkout, OutboxOperationType.SaveSet,
                OutboxOperationType.SaveSet, OutboxOperationType.CompleteWorkout],
            operations.Select(operation => operation.Type));
        Assert.Equal([0L, 1L, 2L, 3L], operations.Select(operation => operation.BaseVersion));

        var api = new RecordingSyncApi(operations, CompletedGraph(completed));
        var sync = new SyncCoordinator(restoredDatabase, api, boundary, clock);
        await sync.RunOnceAsync();
        await sync.RunOnceAsync();

        Assert.Equal(operations.Select(operation => operation.OperationId), api.PushedOperationIds);
        Assert.Equal(operations.Count, api.PushedOperationIds.Distinct().Count());
        var finalHistory = await restoredRepository.GetHistoryAsync();
        Assert.Equal([10, 9], Assert.Single(Assert.Single(finalHistory).Exercises).Sets.Select(set => set.Reps));
        Assert.Empty(await outbox.PendingAsync());
    }

    [Fact]
    public async Task Native_workout_flow_exposes_finish_and_moves_active_graph_to_history()
    {
        var exerciseId = Guid.NewGuid();
        var cachePath = Path.Combine(
            Path.GetTempPath(), $"trackz-finish-cache-{Guid.NewGuid():N}.db");
        try
        {
            var clock = new MutableClock(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));
            var boundary = new AccountSessionBoundary();
            var repository = new LocalWorkoutRepository(new TrackZLocalDatabase(_path));
            var coordinator = new ActiveWorkoutCoordinator(repository, boundary, clock);
            await coordinator.StartAsync([
                new WorkoutExerciseSelection(exerciseId, TrackingMode.Bodyweight)
            ]);
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            await coordinator.SaveSetAsync(exerciseId, new LocalSet(null, null, 12));
            var viewModel = new WorkoutViewModel(
                coordinator,
                new ExerciseCache(cachePath),
                boundary,
                WorkoutResources.English);
            await viewModel.RestoreAsync();

            Assert.True(viewModel.FinishWorkoutCommand.CanExecute(null));
            await viewModel.FinishWorkoutCommand.ExecuteAsync();

            Assert.False(viewModel.HasStarted);
            Assert.Null(await coordinator.RestoreActiveAsync());
            Assert.Single(await repository.GetHistoryAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = cachePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static SyncWorkoutDto CompletedGraph(LocalWorkout workout) => new(
        workout.Id,
        (int)workout.Status,
        workout.StartedAt,
        workout.CompletedAt,
        workout.DeletedAt,
        workout.Version,
        workout.Exercises.Select(exercise => new SyncWorkoutExerciseDto(
            exercise.Id,
            exercise.ExerciseDefinitionId,
            (int)exercise.TrackingMode,
            exercise.Order,
            exercise.DeletedAt,
            exercise.Version,
            exercise.Sets.Select(set => new SyncSetDto(
                set.Id,
                set.Order,
                set.WeightKg?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                set.AssistedKg?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                set.Reps,
                set.CompletedAt,
                set.UpdatedAt,
                set.DeletedAt,
                set.Version)).ToArray())).ToArray());

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    private sealed class RecordingSyncApi(
        IReadOnlyList<OutboxOperation> operations,
        SyncWorkoutDto completed) : ISyncApi
    {
        private int _pushIndex;
        private bool _changeReturned;

        public List<Guid> PushedOperationIds { get; } = [];

        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            var operation = Assert.Single(request.Operations);
            Assert.Equal(operations[_pushIndex].OperationId, operation.OperationId);
            PushedOperationIds.Add(operation.OperationId);
            _pushIndex++;
            return Task.FromResult(new SyncPushResponse([
                new SyncOperationResultDto(
                    operation.OperationId,
                    SyncOperationStatus.Applied,
                    operation.BaseVersion!.Value + 1,
                    null)
            ]));
        }

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default)
        {
            if (_changeReturned) return Task.FromResult(new SyncPullResponse([], cursor, false));
            _changeReturned = true;
            return Task.FromResult(new SyncPullResponse([
                new SyncChangeDto(
                    1,
                    "Workout",
                    completed.Id,
                    completed.Version,
                    false,
                    completed.CompletedAt!.Value,
                    completed)
            ], "cursor-1", false));
        }
    }
}
