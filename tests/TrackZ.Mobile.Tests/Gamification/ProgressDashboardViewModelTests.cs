using TrackZ.Mobile.Features.Gamification;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Gamification;

public sealed class ProgressDashboardViewModelTests
{
    [Fact]
    public async Task Dashboard_uses_one_authoritative_refresh_for_level_streak_badges_and_prs()
    {
        var source = new SequenceSource(Snapshot(11, 640, 3), Snapshot(12, 720, 4));
        var sut = new ProgressDashboardViewModel(
            source,
            new OnlineConnectivity(),
            new KilogramPreference(),
            new AccountSessionBoundary(),
            GamificationResources.English);

        await sut.LoadAsync();

        Assert.Equal(12, sut.Level);
        Assert.Equal(720, sut.TotalXp);
        Assert.Equal(4, sut.CurrentStreakWeeks);
        Assert.Single(sut.Badges);
        Assert.Single(sut.Exercises);
        Assert.Equal(1, source.RefreshCount);
    }

    [Fact]
    public async Task Summary_uses_cached_baseline_then_authoritative_reward_delta()
    {
        var workoutId = Guid.NewGuid();
        var source = new SequenceSource(Snapshot(11, 640, 3), Snapshot(12, 720, 4));
        var sut = new WorkoutSummaryViewModel(
            new SummarySource(new CompletedWorkoutSummary(workoutId, 900m, 2, 18)),
            source,
            new OnlineConnectivity(),
            GamificationResources.English);

        await sut.LoadAsync(workoutId);

        Assert.Equal(80, sut.Reveal!.XpDelta);
        Assert.Equal(11, sut.Reveal.PreviousLevel);
        Assert.Equal(12, sut.Reveal.CurrentLevel);
        Assert.Equal(["consistent-4"], sut.Reveal.NewlyEarnedBadgeKeys);
    }
    [Fact]
    public void Progress_reveal_never_invents_negative_xp_and_preserves_authoritative_badges()
    {
        var reveal = ProgressReveal.Between(
            previousXp: 720,
            previousLevel: 11,
            currentXp: 700,
            currentLevel: 12,
            newlyEarnedBadgeKeys: ["consistent-4"],
            improvedExerciseIds: [Guid.NewGuid()],
            isProvisional: false);

        Assert.Equal(0, reveal.XpDelta);
        Assert.Equal(12, reveal.CurrentLevel);
        Assert.Equal(["consistent-4"], reveal.NewlyEarnedBadgeKeys);
    }

    [Fact]
    public void Streak_accessibility_copy_is_weekly_and_non_punitive()
    {
        Assert.DoesNotContain("daily", GamificationResources.English.StreakAccessibilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("week", GamificationResources.English.StreakAccessibilityText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("สัปดาห์", GamificationResources.Thai.StreakAccessibilityText, StringComparison.Ordinal);
    }

    private static ProgressSnapshot Snapshot(int level, int xp, int streak) => new(
        new ProgressSummaryDto(1000, 500, 4, 1, [new ExerciseProgressSummaryDto(
            Guid.Parse("22222222-2222-2222-2222-222222222222"), "Press", TrackingMode.Weighted,
            DateTimeOffset.UtcNow, 60, null, 8, level == 12 ? 72.5m : 70m, null, 8)]),
        new GamificationProfileDto(
            xp, level, 600, 800, 3, 2, streak, streak,
            [new EarnedBadgeDto("consistent-4", "name", "description", "streak", DateTimeOffset.UtcNow)],
            level == 12 ? ["consistent-4"] : []),
        DateTimeOffset.UtcNow);

    private sealed class SequenceSource(ProgressSnapshot cached, ProgressSnapshot refreshed) : IProgressSnapshotSource
    {
        public int RefreshCount { get; private set; }
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) => Task.FromResult<ProgressSnapshot?>(cached);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) { RefreshCount++; return Task.FromResult(refreshed); }
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) => Task.FromResult(refreshed);
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class KilogramPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }

    private sealed class SummarySource(CompletedWorkoutSummary summary) : ICompletedWorkoutSummarySource
    {
        public Task<CompletedWorkoutSummary?> GetAsync(Guid workoutId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CompletedWorkoutSummary?>(workoutId == summary.WorkoutId ? summary : null);
    }
}
