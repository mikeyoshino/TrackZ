using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Train;

public sealed class LocalTrainDashboardSourceTests
{
    [Fact]
    public async Task Source_counts_only_live_rows_and_returns_three_newest_completed_workouts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-train-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var shoulderId = Guid.NewGuid();
            var backId = Guid.NewGuid();
            var cache = new ExerciseCache(Path.Combine(root, "exercises.db"));
            await cache.ReplaceAllAsync([
                Exercise(shoulderId, "Press", BodyPart.Shoulders, "https://api.trackz.test/media/shoulder"),
                Exercise(backId, "Row", BodyPart.Back, "/api/v1/media/exercise-images/back/thumbnail")
            ], At(10));
            await cache.SetServerThumbnailAsync(backId, "/cache/back.png");

            var active = Workout(LocalWorkoutStatus.Active, At(9), null, [
                ExerciseRow(shoulderId, 0, [Set(0), Set(1, deleted: true)]),
                ExerciseRow(backId, 1, [Set(0)], deleted: true)
            ]);
            var newest = Workout(LocalWorkoutStatus.Completed, At(6), At(9), [
                ExerciseRow(shoulderId, 0, [Set(0)]),
                ExerciseRow(backId, 1, [Set(0)])
            ]);
            var history = new[]
            {
                Workout(LocalWorkoutStatus.Completed, At(5), At(7), []),
                Workout(LocalWorkoutStatus.Completed, At(4), At(6), []),
                Workout(LocalWorkoutStatus.Completed, At(3), At(5), []),
                newest
            };
            var source = new LocalTrainDashboardSource(
                new StubWorkoutRepository(active, history),
                cache);

            var snapshot = await source.LoadAsync();

            Assert.Equal(1, snapshot.Active?.ExerciseCount);
            Assert.Equal(1, snapshot.Active?.LoggedSetCount);
            Assert.Equal(3, snapshot.Recent.Count);
            var first = snapshot.Recent[0];
            Assert.Equal(newest.Id, first.WorkoutId);
            Assert.Equal([BodyPart.Shoulders, BodyPart.Back], first.BodyParts);
            Assert.Equal("/cache/back.png", first.ThumbnailPath);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static ExerciseSummaryDto Exercise(
        Guid id,
        string name,
        BodyPart bodyPart,
        string? thumbnail) =>
        new(id, name, bodyPart, TrackingMode.Weighted, thumbnail, null, null, null, false);

    private static LocalWorkout Workout(
        LocalWorkoutStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        IReadOnlyList<LocalWorkoutExercise> exercises) =>
        new(Guid.NewGuid(), status, startedAt, completedAt, null, 1, 0, exercises);

    private static LocalWorkoutExercise ExerciseRow(
        Guid definitionId,
        int order,
        IReadOnlyList<LocalSet> sets,
        bool deleted = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), definitionId, TrackingMode.Weighted, order,
            deleted ? At(9) : null, 1, 0, sets);

    private static LocalSet Set(int order, bool deleted = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), order, 50, null, 8, At(8), null,
            deleted ? At(9) : null, 1, 0, Guid.NewGuid());

    private static DateTimeOffset At(int hour) =>
        new(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);

    private sealed class StubWorkoutRepository(
        LocalWorkout? active,
        IReadOnlyList<LocalWorkout> history) : ILocalWorkoutRepository
    {
        public Task<LocalWorkout?> GetActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(active);
        public Task<IReadOnlyList<LocalWorkout>> GetHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(history);
        public Task SaveWorkoutAndEnqueueAsync(LocalWorkout workout, OutboxOperation operation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsExerciseHistorySessionInvalidatedAsync(Guid workoutId, Guid exerciseDefinitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OutboxOperation?> GetOperationAsync(Guid operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset?> GetLatestOperationCreatedAtAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearPrivateDataAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
