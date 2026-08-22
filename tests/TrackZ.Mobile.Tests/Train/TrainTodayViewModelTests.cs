using System.Globalization;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Train;

public sealed class TrainTodayViewModelTests
{
    [Fact]
    public async Task Local_state_commits_before_gated_progress_refresh_and_cached_values_survive_failure()
    {
        var source = new RecordingTrainDashboardSource(new(
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), At(9), [BodyPart.Chest], 2, 1, 4),
            null));
        var progress = new CachedThenGatedFailingProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640));
        var viewModel = CreateViewModel(source, progress, online: true);

        var load = viewModel.LoadAsync();
        await progress.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(viewModel.ActiveWorkout);
        Assert.Equal(3, viewModel.WeeklyCompletedWorkouts);
        Assert.Equal(8, viewModel.Level);
        progress.FailRefresh();
        await load;

        Assert.True(viewModel.HasAuthoritativeProgress);
        Assert.Equal(640, viewModel.TotalXp);
    }

    [Fact]
    public async Task No_cache_offline_hides_motivation_instead_of_fabricating_zeroes()
    {
        var viewModel = CreateViewModel(new RecordingTrainDashboardSource(new(null, null)), new EmptyProgressSource(), online: false);

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.False(viewModel.HasRecentMomentum);
    }

    [Fact]
    public async Task Progress_cache_read_failure_keeps_local_workout_and_hides_unauthoritative_motivation()
    {
        var source = new RecordingTrainDashboardSource(new(
            new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), At(9), [BodyPart.Chest], 2, 1, 4),
            null));
        var viewModel = CreateViewModel(source, new ThrowingProgressSource(), online: false);

        await viewModel.LoadAsync();

        Assert.NotNull(viewModel.ActiveWorkout);
        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.Null(viewModel.ErrorText);
    }

    [Fact]
    public async Task Caller_cancellation_during_progress_read_is_propagated()
    {
        var progress = new GatedCancellableProgressSource();
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(null, null)),
            progress,
            online: false);
        using var cancellation = new CancellationTokenSource();

        var load = viewModel.LoadAsync(cancellation.Token);
        await progress.CacheEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
    }

    [Fact]
    public async Task Recent_momentum_uses_latest_time_then_descending_exercise_id()
    {
        var earliest = ProgressRecord("11111111-1111-1111-1111-111111111111", "Early", At(8));
        var lowerTie = ProgressRecord("22222222-2222-2222-2222-222222222222", "Lower tie", At(10));
        var higherTie = ProgressRecord("33333333-3333-3333-3333-333333333333", "Higher tie", At(10));
        var snapshot = Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640) with
        {
            Summary = new ProgressSummaryDto(1000m, 500m, 3, 1, [earliest, lowerTie, higherTie])
        };
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(null, null)),
            new CachedProgressSource(snapshot),
            online: false);

        await viewModel.LoadAsync();

        Assert.Equal("Higher tie", viewModel.RecentMomentum?.ExerciseName);
    }

    [Fact]
    public async Task Account_reset_after_cached_progress_prevents_delayed_refresh_from_restoring_home_state()
    {
        var boundary = new AccountSessionBoundary();
        var source = new RecordingTrainDashboardSource(new(
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"), At(9), [BodyPart.Chest], 2, 1, 4),
            new(Guid.Parse("55555555-5555-5555-5555-555555555555"), [BodyPart.Back], At(8), 2, 4, null,
                [new WorkoutExerciseSelection(Guid.Parse("66666666-6666-6666-6666-666666666666"), TrackingMode.Weighted)])));
        var progress = new CachedThenGatedProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640));
        var viewModel = CreateViewModel(source, progress, online: true, boundary);

        var load = viewModel.LoadAsync();
        await progress.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await boundary.ResetAsync(_ => Task.CompletedTask);
        progress.Release(Snapshot(goal: 5, done: 5, streak: 9, level: 99, xp: 9999));
        await load;

        Assert.Null(viewModel.ActiveWorkout);
        Assert.Null(viewModel.RepeatWorkout);
        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.Equal(0, viewModel.Level);
        Assert.Equal(0, viewModel.TotalXp);
        Assert.False(viewModel.HasRecentMomentum);
    }
    [Fact]
    public async Task Today_loads_active_workout_and_exact_repeat_shortcut()
    {
        var activeId = Guid.NewGuid();
        var repeatId = Guid.NewGuid();
        var source = new RecordingTrainDashboardSource(new(
            new(activeId, At(9), [BodyPart.Chest], 2, 1, 4),
            new(repeatId, [BodyPart.Shoulders, BodyPart.Back], At(8), 6, 18,
                "/cache/shoulder.png", [
                    new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted),
                    new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Assisted)
                ])));
        var viewModel = new TrainTodayViewModel(
            source,
            new AccountSessionBoundary(),
            WorkoutResources.English);

        await viewModel.LoadAsync();

        Assert.Equal(activeId, viewModel.ActiveWorkout?.WorkoutId);
        Assert.Equal(4, viewModel.ActiveWorkout?.LoggedSetCount);
        Assert.Equal(repeatId, viewModel.RepeatWorkout?.SourceWorkoutId);
        Assert.Single(viewModel.RecentWorkouts);
        Assert.Equal("Shoulders + Back", viewModel.RecentWorkouts[0].Title);
        Assert.Equal(1, source.LoadCount);
    }

    [Theory]
    [InlineData("en-US", "Shoulders + Back")]
    [InlineData("th-TH", "ไหล่ + หลัง")]
    public async Task Recent_title_uses_current_localized_body_part_names(
        string cultureName,
        string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var source = new RecordingTrainDashboardSource(new(
                null,
                new(Guid.NewGuid(), [BodyPart.Shoulders, BodyPart.Back], At(8), 2, 1, null,
                    [new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)])));
            var viewModel = new TrainTodayViewModel(
                source,
                new AccountSessionBoundary(),
                WorkoutResources.Current);

            await viewModel.LoadAsync();

            Assert.Equal(expected, Assert.Single(viewModel.RecentWorkouts).Title);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Account_reset_during_load_does_not_commit_stale_dashboard()
    {
        var boundary = new AccountSessionBoundary();
        var source = new GatedTrainDashboardSource();
        var viewModel = new TrainTodayViewModel(source, boundary, WorkoutResources.English);
        var load = viewModel.LoadAsync();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await boundary.ResetAsync(_ => Task.CompletedTask);
        source.Release.TrySetResult(new TrainDashboardSnapshot(
            new(Guid.NewGuid(), At(9), [BodyPart.Chest], 1, 1, 1),
            null));
        await load;

        Assert.Null(viewModel.ActiveWorkout);
        Assert.Empty(viewModel.RecentWorkouts);
    }

    private static DateTimeOffset At(int hour) =>
        new(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);

    private static TrainTodayViewModel CreateViewModel(
        ITrainDashboardSource source,
        IProgressSnapshotSource progress,
        bool online,
        IAccountSessionBoundary? boundary = null) => new(
        source,
        boundary ?? new AccountSessionBoundary(),
        WorkoutResources.English,
        progress,
        new FixedConnectivity(online),
        new MutableWeightPreference(),
        GamificationResources.English);

    private static ProgressSnapshot Snapshot(int goal, int done, int streak, int level, int xp) => new(
        new ProgressSummaryDto(
            1000m,
            500m,
            done,
            1,
            [new ExerciseProgressSummaryDto(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "Bench Press",
                TrackingMode.Weighted,
                At(10),
                70.125m,
                null,
                8,
                72.5m,
                null,
                6)]),
        new GamificationProfileDto(xp, level, 600, 800, goal, done, streak, streak, [], []),
        At(11));

    private static ExerciseProgressSummaryDto ProgressRecord(string id, string name, DateTimeOffset performedAt) => new(
        Guid.Parse(id), name, TrackingMode.Weighted, performedAt, 70m, null, 8, 72m, null, 6);

    private sealed class RecordingTrainDashboardSource(TrainDashboardSnapshot snapshot)
        : ITrainDashboardSource
    {
        public int LoadCount { get; private set; }

        public Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class GatedTrainDashboardSource : ITrainDashboardSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TrainDashboardSnapshot> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class EmptyProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(null);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("No cached progress."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class CachedProgressSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(snapshot);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class ThrowingProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot?>(new InvalidOperationException("Progress cache unavailable."));

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("Progress unavailable."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class GatedCancellableProgressSource : IProgressSnapshotSource
    {
        public TaskCompletionSource CacheEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
        {
            CacheEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("Not reached."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class CachedThenGatedFailingProgressSource(ProgressSnapshot cached) : IProgressSnapshotSource
    {
        private readonly TaskCompletionSource<ProgressSnapshot> _refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(cached);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshEntered.TrySetResult();
            return _refresh.Task;
        }

        public void FailRefresh() => _refresh.TrySetException(new InvalidOperationException("Refresh unavailable."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class CachedThenGatedProgressSource(ProgressSnapshot cached) : IProgressSnapshotSource
    {
        private readonly TaskCompletionSource<ProgressSnapshot> _refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(cached);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshEntered.TrySetResult();
            return _refresh.Task;
        }

        public void Release(ProgressSnapshot refreshed) => _refresh.TrySetResult(refreshed);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class FixedConnectivity(bool online) : IConnectivityService
    {
        public bool IsOnline => online;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MutableWeightPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }
}
