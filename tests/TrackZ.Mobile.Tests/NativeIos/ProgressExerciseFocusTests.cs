using System.Globalization;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ProgressExerciseFocusTests
{
    [Fact]
    public async Task Requested_exercise_is_matched_once_after_progress_load()
    {
        using var dispatcher = new DispatcherScope();
        var requestedExerciseId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var viewModel = CreateViewModel();
        var page = new ExerciseProgressPage(viewModel);

        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["exerciseId"] = requestedExerciseId.ToString("D", CultureInfo.InvariantCulture)
        });
        await viewModel.LoadAsync();

        Assert.Equal(requestedExerciseId, page.ConsumeRequestedExercise()!.ExerciseId);
        Assert.Null(page.ConsumeRequestedExercise());
    }

    [Fact]
    public async Task Focus_waits_for_the_mapped_generated_row_to_receive_layout_before_nonanimated_scroll()
    {
        using var dispatcher = new DispatcherScope();
        var requestedExerciseId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var scrollRequest = new TaskCompletionSource<ScrollRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel();
        var page = new ExerciseProgressPage(
            viewModel,
            (scroll, x, y, animated) =>
            {
                scrollRequest.TrySetResult(new ScrollRequest(scroll, x, y, animated));
                return Task.CompletedTask;
            });
        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["exerciseId"] = requestedExerciseId.ToString("D", CultureInfo.InvariantCulture)
        });
        await viewModel.LoadAsync();

        var rows = page.FindByName<VerticalStackLayout>("ExerciseProgressRows");
        var requestedRow = Assert.IsAssignableFrom<VisualElement>(rows.Children[1]);
        Assert.True(requestedRow.Width <= 0 || requestedRow.Height <= 0);
        using var cancellation = new CancellationTokenSource();

        var focus = page.FocusRequestedExerciseAsync(cancellation.Token);

        Assert.False(scrollRequest.Task.IsCompleted);
        requestedRow.Arrange(new Rect(0, 240, 390, 120));

        var request = await scrollRequest.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await focus;
        Assert.Same(page.FindByName<ScrollView>("ExerciseProgressScroll"), request.Scroll);
        Assert.Equal(0, request.X);
        Assert.Equal(240, request.Y);
        Assert.False(request.Animated);
    }

    [Fact]
    public async Task Overlapping_appearances_keep_the_latest_focus_request_after_the_first_is_cancelled()
    {
        using var dispatcher = new DispatcherScope();
        var source = new GatedFirstReadProgressSnapshotSource(CreateSnapshot());
        var scrollRequest = new TaskCompletionSource<ScrollRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new ExerciseProgressPage(
            CreateViewModel(source),
            (scroll, x, y, animated) =>
            {
                scrollRequest.TrySetResult(new ScrollRequest(scroll, x, y, animated));
                return Task.CompletedTask;
            });
        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["exerciseId"] = "66666666-6666-6666-6666-666666666666"
        });

        var firstAppearance = page.HandleAppearingAsync();
        await source.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["exerciseId"] = "77777777-7777-7777-7777-777777777777"
        });

        var secondAppearance = page.HandleAppearingAsync();
        var rows = page.FindByName<VerticalStackLayout>("ExerciseProgressRows");
        var latestRequestedRow = Assert.IsAssignableFrom<VisualElement>(rows.Children[1]);
        latestRequestedRow.Arrange(new Rect(0, 240, 390, 120));

        var request = await scrollRequest.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.WhenAll(firstAppearance, secondAppearance);

        Assert.Equal(240, request.Y);
        Assert.False(request.Animated);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task Invalid_requested_exercise_is_not_consumed(string exerciseId)
    {
        using var dispatcher = new DispatcherScope();
        var page = new ExerciseProgressPage(CreateViewModel());

        page.ApplyQueryAttributes(new Dictionary<string, object> { ["exerciseId"] = exerciseId });

        Assert.Null(page.ConsumeRequestedExercise());
    }

    [Fact]
    public void Missing_requested_exercise_is_not_consumed()
    {
        using var dispatcher = new DispatcherScope();
        var page = new ExerciseProgressPage(CreateViewModel());

        page.ApplyQueryAttributes(new Dictionary<string, object>());

        Assert.Null(page.ConsumeRequestedExercise());
    }

    private static ProgressDashboardViewModel CreateViewModel(IProgressSnapshotSource? source = null) => new(
        source ?? new FixedProgressSnapshotSource(CreateSnapshot()),
        new OfflineConnectivity(),
        new KilogramPreference(),
        new AccountSessionBoundary(),
        GamificationResources.English);

    private static ProgressSnapshot CreateSnapshot() => new(
            new ProgressSummaryDto(
                1000m,
                500m,
                2,
                1,
                [
                    Exercise("66666666-6666-6666-6666-666666666666", "Back Squat"),
                    Exercise("77777777-7777-7777-7777-777777777777", "Bench Press")
                ]),
            new GamificationProfileDto(640, 8, 600, 800, 3, 2, 4, 4, [], []),
            new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.Zero));

    private static ExerciseProgressSummaryDto Exercise(string id, string name) => new(
        Guid.Parse(id),
        name,
        TrackingMode.Weighted,
        new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero),
        70m,
        null,
        8,
        72.5m,
        null,
        6);

    private sealed class FixedProgressSnapshotSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(snapshot);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class GatedFirstReadProgressSnapshotSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        private int _readCount;

        public TaskCompletionSource FirstReadStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                FirstReadStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return snapshot;
        }

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class KilogramPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }

    private sealed record ScrollRequest(ScrollView Scroll, double X, double Y, bool Animated);

    private sealed class DispatcherScope : IDisposable
    {
        private readonly IDispatcherProvider _original = DispatcherProvider.Current;

        public DispatcherScope() => DispatcherProvider.SetCurrent(new InlineDispatcherProvider());

        public void Dispose() => DispatcherProvider.SetCurrent(_original);
    }

    private sealed class InlineDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new InlineDispatcher();
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new InlineTimer();
    }

    private sealed class InlineTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
