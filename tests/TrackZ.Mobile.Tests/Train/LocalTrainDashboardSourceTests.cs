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
    private static readonly Guid WeightedId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid AssistedId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid BodyweightId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid MissingId = Guid.Parse("10000000-0000-0000-0000-000000000004");

    [Fact]
    public async Task Source_returns_exact_active_progress_and_newest_repeatable_selection_order()
    {
        await using var fixture = await CreateSourceWithMixedModes();

        var snapshot = await fixture.Source.LoadAsync();
        var active = Assert.IsType<ActiveWorkoutCard>(snapshot.Active);

        Assert.Equal([BodyPart.Chest, BodyPart.Back], active.BodyParts);
        Assert.Equal(4, active.ExerciseCount);
        Assert.Equal(3, active.LoggedExerciseCount);
        Assert.Equal(5, active.LoggedSetCount);
        Assert.Equal([WeightedId, AssistedId, BodyweightId],
            snapshot.Repeat!.Selections.Select(x => x.ExerciseDefinitionId));
        Assert.Equal([TrackingMode.Weighted, TrackingMode.Assisted, TrackingMode.Bodyweight],
            snapshot.Repeat.Selections.Select(x => x.TrackingMode));
        Assert.Equal(5, snapshot.Repeat.LoggedSetCount);
    }

    [Fact]
    public async Task Source_refuses_partial_repeat_when_any_live_definition_is_missing_or_mode_changed()
    {
        await using var missing = await CreateSourceWithMissingDefinition();
        await using var changedMode = await CreateSourceWithChangedDefinitionMode();

        Assert.Null((await missing.Source.LoadAsync()).Repeat);
        Assert.Null((await changedMode.Source.LoadAsync()).Repeat);
    }

    [Fact]
    public async Task Source_skips_a_newer_inexact_workout_and_returns_the_next_exact_repeat()
    {
        await using var fixture = await CreateSourceWithNewerInexactAndOlderExactWorkouts();

        var snapshot = await fixture.Source.LoadAsync();

        Assert.Equal(fixture.OlderExactWorkoutId, snapshot.Repeat!.SourceWorkoutId);
        Assert.Equal([WeightedId, AssistedId],
            snapshot.Repeat.Selections.Select(x => x.ExerciseDefinitionId));
    }

    [Fact]
    public async Task Source_breaks_repeat_completion_ties_by_larger_workout_id()
    {
        var lowerWorkoutId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var higherWorkoutId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        await using var fixture = await SourceFixture.CreateAsync([
            Exercise(WeightedId, "Weighted Press", BodyPart.Chest, TrackingMode.Weighted, null)
        ]);
        var lower = Workout(LocalWorkoutStatus.Completed, At(5), At(8), [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0)])
        ], lowerWorkoutId);
        var higher = Workout(LocalWorkoutStatus.Completed, At(6), At(8), [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0)])
        ], higherWorkoutId);
        fixture.Source = new LocalTrainDashboardSource(
            new StubWorkoutRepository(null, [lower, higher]), fixture.Cache);

        var snapshot = await fixture.Source.LoadAsync();

        Assert.Equal(higherWorkoutId, snapshot.Repeat!.SourceWorkoutId);
    }

    private static async Task<SourceFixture> CreateSourceWithMixedModes()
    {
        var fixture = await SourceFixture.CreateAsync([
            Exercise(WeightedId, "Weighted Press", BodyPart.Chest, TrackingMode.Weighted, "/cache/weighted.png"),
            Exercise(AssistedId, "Assisted Row", BodyPart.Back, TrackingMode.Assisted, "/cache/assisted.png"),
            Exercise(BodyweightId, "Bodyweight Push-up", BodyPart.Chest, TrackingMode.Bodyweight, "/cache/bodyweight.png")
        ]);
        var active = Workout(LocalWorkoutStatus.Active, At(9), null, [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0), Set(1), Set(2, deleted: true)]),
            ExerciseRow(AssistedId, TrackingMode.Assisted, 1, [Set(0), Set(1)]),
            ExerciseRow(BodyweightId, TrackingMode.Bodyweight, 2, [Set(0, deleted: true)]),
            ExerciseRow(MissingId, TrackingMode.Weighted, 3, [Set(0)]),
            ExerciseRow(WeightedId, TrackingMode.Weighted, 4, [Set(0)], deleted: true)
        ]);
        var completed = Workout(LocalWorkoutStatus.Completed, At(6), At(8), [
            ExerciseRow(BodyweightId, TrackingMode.Bodyweight, 2, [Set(0), Set(1)]),
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0), Set(1)]),
            ExerciseRow(AssistedId, TrackingMode.Assisted, 1, [Set(0)])
        ]);
        fixture.Source = new LocalTrainDashboardSource(
            new StubWorkoutRepository(active, [completed]), fixture.Cache);
        return fixture;
    }

    private static async Task<SourceFixture> CreateSourceWithMissingDefinition()
    {
        var fixture = await SourceFixture.CreateAsync([
            Exercise(WeightedId, "Weighted Press", BodyPart.Chest, TrackingMode.Weighted, null)
        ]);
        var completed = Workout(LocalWorkoutStatus.Completed, At(6), At(8), [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0)]),
            ExerciseRow(MissingId, TrackingMode.Bodyweight, 1, [Set(0)])
        ]);
        fixture.Source = new LocalTrainDashboardSource(
            new StubWorkoutRepository(null, [completed]), fixture.Cache);
        return fixture;
    }

    private static async Task<SourceFixture> CreateSourceWithChangedDefinitionMode()
    {
        var fixture = await SourceFixture.CreateAsync([
            Exercise(WeightedId, "Weighted Press", BodyPart.Chest, TrackingMode.Assisted, null)
        ]);
        var completed = Workout(LocalWorkoutStatus.Completed, At(6), At(8), [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0)])
        ]);
        fixture.Source = new LocalTrainDashboardSource(
            new StubWorkoutRepository(null, [completed]), fixture.Cache);
        return fixture;
    }

    private static async Task<SourceFixture> CreateSourceWithNewerInexactAndOlderExactWorkouts()
    {
        var fixture = await SourceFixture.CreateAsync([
            Exercise(WeightedId, "Weighted Press", BodyPart.Chest, TrackingMode.Weighted, null),
            Exercise(AssistedId, "Assisted Row", BodyPart.Back, TrackingMode.Assisted, null)
        ]);
        var olderExact = Workout(LocalWorkoutStatus.Completed, At(5), At(7), [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0)]),
            ExerciseRow(AssistedId, TrackingMode.Assisted, 1, [Set(0)])
        ]);
        var newerInexact = Workout(LocalWorkoutStatus.Completed, At(6), At(8), [
            ExerciseRow(WeightedId, TrackingMode.Weighted, 0, [Set(0)]),
            ExerciseRow(MissingId, TrackingMode.Bodyweight, 1, [Set(0)])
        ]);
        fixture.OlderExactWorkoutId = olderExact.Id;
        fixture.Source = new LocalTrainDashboardSource(
            new StubWorkoutRepository(null, [olderExact, newerInexact]), fixture.Cache);
        return fixture;
    }

    private static ExerciseSummaryDto Exercise(
        Guid id,
        string name,
        BodyPart bodyPart,
        TrackingMode trackingMode,
        string? thumbnail) =>
        new(id, name, bodyPart, trackingMode, thumbnail, null, null, null, false);

    private static LocalWorkout Workout(
        LocalWorkoutStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        IReadOnlyList<LocalWorkoutExercise> exercises,
        Guid? workoutId = null) =>
        new(workoutId ?? Guid.NewGuid(), status, startedAt, completedAt, null, 1, 0, exercises);

    private static LocalWorkoutExercise ExerciseRow(
        Guid definitionId,
        TrackingMode trackingMode,
        int order,
        IReadOnlyList<LocalSet> sets,
        bool deleted = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), definitionId, trackingMode, order,
            deleted ? At(9) : null, 1, 0, sets);

    private static LocalSet Set(int order, bool deleted = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), order, 50, null, 8, At(8), null,
            deleted ? At(9) : null, 1, 0, Guid.NewGuid());

    private static DateTimeOffset At(int hour) =>
        new(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);

    private sealed class SourceFixture : IAsyncDisposable
    {
        private readonly string _root;

        private SourceFixture(string root, ExerciseCache cache)
        {
            _root = root;
            Cache = cache;
        }

        public ExerciseCache Cache { get; }
        public LocalTrainDashboardSource Source { get; set; } = null!;
        public Guid OlderExactWorkoutId { get; set; }

        public static async Task<SourceFixture> CreateAsync(IReadOnlyList<ExerciseSummaryDto> definitions)
        {
            var root = Path.Combine(Path.GetTempPath(), $"trackz-train-source-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var cache = new ExerciseCache(Path.Combine(root, "exercises.db"));
            await cache.ReplaceAllAsync(definitions, At(10));
            return new SourceFixture(root, cache);
        }

        public ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

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
