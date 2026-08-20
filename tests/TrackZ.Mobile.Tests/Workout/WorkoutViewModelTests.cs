using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class WorkoutViewModelTests
{
    private sealed class MemoryPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    [Fact]
    public async Task Restored_active_workout_reports_only_sets_actually_logged()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(fixture.SecondId, TrackingMode.Bodyweight)
        ]);
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(50, null, 8));
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(55, null, 6));
        var viewModel = fixture.CreateViewModel();

        await viewModel.RestoreAsync();

        Assert.Equal([2, 0], viewModel.Exercises.Select(item => item.LoggedSetCount));
        Assert.All(viewModel.Exercises, item =>
            Assert.DoesNotContain(" of ", item.AccessibilitySummary, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Active_reorder_and_remove_remain_durable_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted),
            new WorkoutExerciseSelection(fixture.SecondId, TrackingMode.Bodyweight),
            new WorkoutExerciseSelection(fixture.ThirdId, TrackingMode.Assisted)
        ]);
        var viewModel = fixture.CreateViewModel();
        await viewModel.RestoreAsync();
        var second = viewModel.Exercises.Single(item => item.ExerciseDefinitionId == fixture.SecondId);
        var third = viewModel.Exercises.Single(item => item.ExerciseDefinitionId == fixture.ThirdId);

        await viewModel.MoveUpCommand.ExecuteAsync(third);
        third = viewModel.Exercises.Single(item => item.ExerciseDefinitionId == fixture.ThirdId);
        await viewModel.MoveUpCommand.ExecuteAsync(third);
        await viewModel.RemoveExerciseCommand.ExecuteAsync(second);
        var restored = fixture.CreateViewModel();
        await restored.RestoreAsync();

        Assert.Equal(
            [fixture.ThirdId, fixture.FirstId],
            restored.Exercises.Select(item => item.ExerciseDefinitionId));
    }

    [Fact]
    public async Task Restored_last_performance_follows_the_shared_weight_unit_preference()
    {
        await using var fixture = await Fixture.CreateAsync(lastWeightKg: 70.125m);
        await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        var preferences = new WeightUnitPreference(new MemoryPreferenceStore());
        var viewModel = fixture.CreateViewModel(preferences);

        await viewModel.RestoreAsync();
        Assert.Contains("70.125", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);
        Assert.Contains("kg", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);

        preferences.Set(WeightDisplayUnit.Pounds);

        Assert.Contains("154.60", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);
        Assert.Contains("lb", viewModel.Exercises.Single().LastText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finish_notifies_navigation_only_after_the_completed_workout_is_durable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var started = await fixture.Coordinator.StartAsync([
            new WorkoutExerciseSelection(fixture.FirstId, TrackingMode.Weighted)
        ]);
        await fixture.Coordinator.SaveSetAsync(fixture.FirstId, new LocalSet(50, null, 8));
        var viewModel = fixture.CreateViewModel();
        await viewModel.RestoreAsync();
        Guid? notified = null;
        viewModel.WorkoutFinished += (_, workoutId) => notified = workoutId;

        await viewModel.FinishWorkoutCommand.ExecuteAsync();

        Assert.Equal(started.Id, notified);
        Assert.Equal(started.Id, viewModel.CompletedWorkoutId);
        Assert.Null(await fixture.Coordinator.RestoreActiveAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly AccountSessionBoundary _boundary;
        private readonly ExerciseCache _cache;

        private Fixture(
            string root,
            AccountSessionBoundary boundary,
            ExerciseCache cache,
            ActiveWorkoutCoordinator coordinator,
            Guid firstId,
            Guid secondId,
            Guid thirdId)
        {
            _root = root;
            _boundary = boundary;
            _cache = cache;
            Coordinator = coordinator;
            FirstId = firstId;
            SecondId = secondId;
            ThirdId = thirdId;
        }

        public ActiveWorkoutCoordinator Coordinator { get; }
        public Guid FirstId { get; }
        public Guid SecondId { get; }
        public Guid ThirdId { get; }

        public static async Task<Fixture> CreateAsync(decimal? lastWeightKg = null)
        {
            var root = Path.Combine(Path.GetTempPath(), $"trackz-workout-vm-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var third = Guid.NewGuid();
            var cache = new ExerciseCache(Path.Combine(root, "exercises.db"));
            await cache.ReplaceAllAsync([
                Summary(first, "Press", TrackingMode.Weighted, lastWeightKg),
                Summary(second, "Pull-up", TrackingMode.Bodyweight),
                Summary(third, "Assisted Dip", TrackingMode.Assisted)
            ], DateTimeOffset.UtcNow);
            var boundary = new AccountSessionBoundary();
            var repository = new LocalWorkoutRepository(
                new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            return new Fixture(
                root,
                boundary,
                cache,
                new ActiveWorkoutCoordinator(repository, boundary, new Clock()),
                first,
                second,
                third);
        }

        public WorkoutViewModel CreateViewModel(IWeightUnitPreference? unitPreference = null) =>
            new(Coordinator, _cache, _boundary, WorkoutResources.English, unitPreference: unitPreference);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }

        private static ExerciseSummaryDto Summary(
            Guid id,
            string name,
            TrackingMode mode,
            decimal? lastWeightKg = null) =>
            new(
                id,
                name,
                BodyPart.Chest,
                mode,
                null,
                null,
                lastWeightKg is null ? null : new PerformanceSetDto(lastWeightKg, null, 8),
                null,
                false);

        private sealed class Clock : IClock
        {
            private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
            public DateTimeOffset UtcNow => new(Interlocked.Increment(ref _ticks), TimeSpan.Zero);
        }
    }
}
