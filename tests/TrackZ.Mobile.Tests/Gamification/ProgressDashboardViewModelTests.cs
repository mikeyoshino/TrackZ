using TrackZ.Mobile.Features.Gamification;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using System.Globalization;

namespace TrackZ.Mobile.Tests.Gamification;

public sealed class ProgressDashboardViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Progress_remains_unavailable_without_a_real_snapshot_even_when_loading_fails(bool throwOnRead)
    {
        var source = new MaybeSnapshotSource(snapshot: null, throwOnRead);
        var dashboard = new ProgressDashboardViewModel(
            source,
            new OfflineConnectivity(),
            new KilogramPreference(),
            new AccountSessionBoundary(),
            GamificationResources.English);
        var workoutId = Guid.NewGuid();
        var summary = new WorkoutSummaryViewModel(
            new SummarySource(new CompletedWorkoutSummary(workoutId, 500m, 1, 8)),
            source,
            new OfflineConnectivity(),
            GamificationResources.English);

        Assert.False(dashboard.HasAuthoritativeProgressData);
        Assert.False(summary.HasAuthoritativeProgressData);
        Assert.Equal(0d, dashboard.LevelProgress);
        Assert.Equal(0d, summary.LevelProgress);

        await dashboard.LoadAsync();
        await summary.LoadAsync(workoutId);

        Assert.False(dashboard.HasAuthoritativeProgressData);
        Assert.False(summary.HasAuthoritativeProgressData);
        Assert.Equal(0d, dashboard.LevelProgress);
        Assert.Equal(0d, summary.LevelProgress);
    }

    [Fact]
    public async Task Real_zero_xp_level_one_snapshot_is_authoritative_and_visible()
    {
        var snapshot = Snapshot(1, 0, 0) with
        {
            Profile = Snapshot(1, 0, 0).Profile with
            {
                CurrentLevelRequiredXp = 0,
                NextLevelRequiredXp = 100
            }
        };
        var source = new MaybeSnapshotSource(snapshot, throwOnRead: false);
        var dashboard = new ProgressDashboardViewModel(
            source,
            new OfflineConnectivity(),
            new KilogramPreference(),
            new AccountSessionBoundary(),
            GamificationResources.English);
        var workoutId = Guid.NewGuid();
        var summary = new WorkoutSummaryViewModel(
            new SummarySource(new CompletedWorkoutSummary(workoutId, 0m, 0, 0)),
            source,
            new OfflineConnectivity(),
            GamificationResources.English);

        await dashboard.LoadAsync();
        await summary.LoadAsync(workoutId);

        Assert.True(dashboard.HasAuthoritativeProgressData);
        Assert.True(summary.HasAuthoritativeProgressData);
        Assert.Equal(1, dashboard.Level);
        Assert.Equal(1, summary.Level);
        Assert.Equal(0, dashboard.TotalXp);
        Assert.Equal(0, summary.TotalXp);
        Assert.Equal(0d, dashboard.LevelProgress);
        Assert.Equal(0d, summary.LevelProgress);
    }

    [Fact]
    public async Task Dashboard_keeps_exact_authoritative_cache_when_the_following_refresh_fails()
    {
        var cached = Snapshot(11, 640, 3);
        var sut = new ProgressDashboardViewModel(
            new CachedThenFailingRefreshSource(cached),
            new OnlineConnectivity(),
            new KilogramPreference(),
            new AccountSessionBoundary(),
            GamificationResources.English);

        await sut.LoadAsync();

        Assert.True(sut.HasAuthoritativeProgressData);
        Assert.Equal(11, sut.Level);
        Assert.Equal(640, sut.TotalXp);
        Assert.Equal(0.2d, sut.LevelProgress, 3);
        Assert.Equal(GamificationResources.English.LoadFailed, sut.ErrorMessage);
    }

    [Fact]
    public async Task Summary_keeps_exact_authoritative_cache_when_the_following_refresh_fails()
    {
        var workoutId = Guid.NewGuid();
        var cached = Snapshot(11, 640, 3);
        var sut = new WorkoutSummaryViewModel(
            new SummarySource(new CompletedWorkoutSummary(workoutId, 900m, 2, 18)),
            new CachedThenFailingRefreshSource(cached),
            new OnlineConnectivity(),
            GamificationResources.English);

        await sut.LoadAsync(workoutId);

        Assert.True(sut.HasAuthoritativeProgressData);
        Assert.Equal(11, sut.Level);
        Assert.Equal(640, sut.TotalXp);
        Assert.Equal(0.2d, sut.LevelProgress, 3);
        Assert.Equal(GamificationResources.English.LoadFailed, sut.ErrorMessage);
    }

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
        Assert.True(sut.HasAuthoritativeProgressData);
    }

    [Fact]
    public async Task Account_reset_during_cached_load_does_not_restore_the_previous_accounts_progress()
    {
        var boundary = new AccountSessionBoundary();
        var source = new GatedCachedSource(Snapshot(11, 640, 3));
        var sut = new ProgressDashboardViewModel(
            source,
            new OfflineConnectivity(),
            new KilogramPreference(),
            boundary,
            GamificationResources.English);

        var load = sut.LoadAsync();
        await source.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await boundary.ResetAsync(_ => Task.CompletedTask);
        source.Release();
        await load;

        Assert.False(sut.HasAuthoritativeProgressData);
        Assert.Empty(sut.Badges);
        Assert.Empty(sut.Exercises);
        Assert.Equal(1, sut.Level);
        Assert.Equal(0, sut.TotalXp);
        Assert.Equal(0, sut.WeeklyCompletedWorkouts);
        Assert.False(sut.IsProgressProvisional);
        Assert.False(sut.IsBusy);
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
        Assert.Equal(0.6d, sut.LevelProgress, 3);
        Assert.True(sut.IsProgressRevealConfirmed);
        Assert.False(sut.IsProgressRevealPending);
        Assert.True(sut.HasAuthoritativeProgressData);
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
        Assert.Equal("Personal records", GamificationResources.English.PersonalRecords);
        Assert.Equal("สถิติส่วนตัว", GamificationResources.Thai.PersonalRecords);
    }

    [Fact]
    public void Badge_presentation_localizes_contract_keys_and_maps_the_real_icon_key()
    {
        var dto = new EarnedBadgeDto(
            "streak-4", "Badge_Streak4_Name", "Badge_Streak4_Description", "badge-streak-4",
            DateTimeOffset.Parse("2026-08-20T06:00:00Z"));

        var english = EarnedBadgePresentation.From(dto, CultureInfo.GetCultureInfo("en-US"));
        var thai = EarnedBadgePresentation.From(dto, CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("4-week streak", english.Name);
        Assert.Equal("Train in four consecutive weeks.", english.Description);
        Assert.Equal("4", english.IconGlyph);
        Assert.Contains("ได้รับ", thai.EarnedText, StringComparison.Ordinal);
        Assert.NotEqual(dto.NameResourceKey, english.Name);
        Assert.NotEqual("★", english.IconGlyph);

        var missingDescription = EarnedBadgePresentation.From(
            dto with { DescriptionResourceKey = "Badge_Unknown_Description" },
            CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(string.Empty, missingDescription.Description);
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

    private sealed class CachedThenFailingRefreshSource(ProgressSnapshot cached) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(cached);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("refresh unavailable"));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class GatedCachedSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _release.Task;
            return snapshot;
        }

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(
            int weeklyGoal,
            CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public void Release() => _release.TrySetResult();
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MaybeSnapshotSource(ProgressSnapshot? snapshot, bool throwOnRead) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            throwOnRead
                ? Task.FromException<ProgressSnapshot?>(new InvalidOperationException("snapshot unavailable"))
                : Task.FromResult(snapshot);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            snapshot is null
                ? Task.FromException<ProgressSnapshot>(new InvalidOperationException("snapshot unavailable"))
                : Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
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
