using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Gamification;

public sealed class GamificationViewModelTests
{
    private static readonly Guid WorkoutId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Offline_summary_renders_local_result_and_marks_server_progress_provisional()
    {
        var source = new StubSnapshotSource(Snapshot());
        var sut = new WorkoutSummaryViewModel(
            new StubWorkoutSummarySource(new CompletedWorkoutSummary(WorkoutId, 1300m, 2, 18)),
            source,
            new StubConnectivity(false),
            GamificationResources.English);

        await sut.LoadAsync(WorkoutId);

        Assert.Equal(1300m, sut.TotalVolumeKg);
        Assert.Equal(2, sut.CompletedSets);
        Assert.True(sut.IsProgressProvisional);
        Assert.Contains("pending", sut.SyncAccessibilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(750, sut.TotalXp);
        Assert.Equal(3, sut.Level);
        Assert.Equal(0d, sut.LevelProgress);
        Assert.True(sut.IsProgressRevealPending);
        Assert.False(sut.IsProgressRevealConfirmed);
        Assert.Equal(0, source.RefreshCount);
    }

    [Fact]
    public async Task Progress_converts_only_presentation_values_and_preserves_canonical_kg()
    {
        var preference = new MutableWeightPreference(WeightDisplayUnit.Pounds);
        var sut = new ExerciseProgressViewModel(
            new StubSnapshotSource(Snapshot()),
            new StubConnectivity(false),
            preference,
            GamificationResources.English);

        await sut.LoadAsync();

        var item = Assert.Single(sut.Exercises);
        Assert.Equal(75.125m, item.BestWeightKg);
        Assert.Equal("165.62 lb × 8", item.PersonalRecordText);
        preference.Set(WeightDisplayUnit.Kilograms);
        Assert.Equal("75.125 kg × 8", item.PersonalRecordText);
    }

    [Fact]
    public async Task Summary_follows_the_shared_weight_unit_without_rewriting_canonical_volume()
    {
        var preference = new MutableWeightPreference(WeightDisplayUnit.Pounds);
        var sut = new WorkoutSummaryViewModel(
            new StubWorkoutSummarySource(new CompletedWorkoutSummary(WorkoutId, 100m, 2, 18)),
            new StubSnapshotSource(Snapshot()),
            new StubConnectivity(false),
            GamificationResources.English,
            preference);

        await sut.LoadAsync(WorkoutId);

        Assert.Equal(100m, sut.TotalVolumeKg);
        Assert.Equal("220.46 lb", sut.TotalVolumeText);
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        sut.Activate();
        preference.Set(WeightDisplayUnit.Kilograms);
        Assert.Equal("100 kg", sut.TotalVolumeText);
        Assert.Contains(nameof(WorkoutSummaryViewModel.TotalVolumeText), changed);

        sut.Deactivate();
        changed.Clear();
        preference.Set(WeightDisplayUnit.Pounds);
        Assert.Empty(changed);
    }

    [Fact]
    public async Task Profile_updates_weekly_goal_with_valid_range_and_refreshes_confirmed_snapshot()
    {
        var source = new StubSnapshotSource(Snapshot());
        var sut = new ProfileViewModel(source, new StubConnectivity(true), GamificationResources.English);
        await sut.LoadAsync();

        sut.WeeklyGoal = 5;
        await sut.SaveWeeklyGoalCommand.ExecuteAsync();

        Assert.Equal([5], source.UpdatedGoals);
        Assert.False(sut.IsProgressProvisional);
        sut.WeeklyGoal = 8;
        Assert.False(sut.SaveWeeklyGoalCommand.CanExecute(null));
    }

    private static ProgressSnapshot Snapshot() => new(
        new ProgressSummaryDto(
            1300m,
            1300m,
            1,
            1,
            [new ExerciseProgressSummaryDto(
                Guid.Parse("20000000-0000-0000-0000-000000000001"),
                "Bench Press",
                TrackingMode.Weighted,
                DateTimeOffset.Parse("2026-08-20T06:00:00Z"),
                70m,
                null,
                10,
                75.125m,
                null,
                8)]),
        new GamificationProfileDto(
            750, 3, 750, 1500, 4, 1, 2, 5,
            [new EarnedBadgeDto("streak-4", "Badge.Streak4.Name", "Badge.Streak4.Description", "streak", DateTimeOffset.Parse("2026-08-20T06:00:00Z"))],
            ["streak-4"]),
        DateTimeOffset.Parse("2026-08-20T06:00:00Z"));

    private sealed class StubSnapshotSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        public int RefreshCount { get; private set; }
        public List<int> UpdatedGoals { get; } = [];
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) => Task.FromResult<ProgressSnapshot?>(snapshot);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(snapshot);
        }
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default)
        {
            UpdatedGoals.Add(weeklyGoal);
            snapshot = snapshot with { Profile = snapshot.Profile with { WeeklyGoal = weeklyGoal } };
            return Task.FromResult(snapshot);
        }
    }

    private sealed class StubWorkoutSummarySource(CompletedWorkoutSummary summary) : ICompletedWorkoutSummarySource
    {
        public Task<CompletedWorkoutSummary?> GetAsync(Guid workoutId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CompletedWorkoutSummary?>(workoutId == summary.WorkoutId ? summary : null);
    }

    private sealed class StubConnectivity(bool isOnline) : IConnectivityService
    {
        public bool IsOnline { get; } = isOnline;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MutableWeightPreference(WeightDisplayUnit current) : IWeightUnitPreference
    {
        public WeightDisplayUnit Current { get; private set; } = current;
        public event EventHandler? Changed;
        public void Set(WeightDisplayUnit unit)
        {
            Current = unit;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
