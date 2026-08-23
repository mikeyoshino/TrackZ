using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class SetEffortSheetTests
{
    [Fact]
    public async Task Present_uses_one_medium_sheet_and_dismiss_completes_once()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter();
        var page = fixture.CreatePage(presenter);

        var pending = page.PresentAsync(
            fixture.Request,
            _ => true,
            CancellationToken.None);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(page, presenter.Page);
        Assert.Equal(NativeSheetDetent.Medium, presenter.Detent);
        await page.DismissAsyncForTest();
        await pending;
        Assert.Equal(1, presenter.DismissCount);
    }

    [Fact]
    public async Task Swipe_disappearance_finishes_without_reopening_or_recording_effort()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter();
        var page = fixture.CreatePage(presenter);
        var before = fixture.Recorder.RecordCalls;
        var pending = page.PresentAsync(fixture.Request, _ => true);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

        page.SimulateDisappearingForTest();
        await pending;

        Assert.Equal(before, fixture.Recorder.RecordCalls);
        Assert.Equal(1, presenter.ShowCount);
    }

    [Fact]
    public async Task Owner_cancellation_dismisses_the_presented_native_sheet_once()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter();
        var page = fixture.CreatePage(presenter);
        using var cancellation = new CancellationTokenSource();
        var pending = page.PresentAsync(
            fixture.Request, _ => true, cancellation.Token);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, presenter.DismissCount);
        Assert.Equal(0, fixture.Recorder.RecordCalls);
    }

    [Fact]
    public async Task Replacement_waits_for_cancelled_presentation_cleanup_then_opens()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter();
        var page = fixture.CreatePage(presenter);
        using var firstOwner = new CancellationTokenSource();
        var first = page.PresentAsync(
            fixture.Request, _ => true, firstOwner.Token);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var replacement = page.PresentAsync(
            fixture.Request with { EffortOperationId = Guid.NewGuid() },
            _ => true,
            CancellationToken.None);
        Assert.Equal(1, presenter.ShowCount);

        firstOwner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await presenter.SecondPresented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, presenter.ShowCount);

        await page.DismissAsyncForTest();
        await replacement;
        Assert.Equal(2, presenter.DismissCount);
    }

    [Fact]
    public async Task Cancelled_replacement_waits_for_in_flight_native_pop_to_finish()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter(delayFirstDismissal: true);
        var page = fixture.CreatePage(presenter);
        using var firstOwner = new CancellationTokenSource();
        var first = page.PresentAsync(fixture.Request, _ => true, firstOwner.Token);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var replacement = page.PresentAsync(
            fixture.Request with { EffortOperationId = Guid.NewGuid() },
            _ => true);

        page.ViewModelForTest.Skip();
        await presenter.DismissStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        firstOwner.Cancel();
        var premature = await Task.WhenAny(
            presenter.SecondPresented.Task,
            Task.Delay(TimeSpan.FromMilliseconds(100)));
        var replacementOpenedBeforePopFinished =
            ReferenceEquals(premature, presenter.SecondPresented.Task);

        presenter.AllowDisappearing.TrySetResult();
        presenter.ReleaseDismissal.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await presenter.SecondPresented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        if (!replacement.IsCompleted)
            await page.DismissAsyncForTest();
        await replacement;

        Assert.False(replacementOpenedBeforePopFinished);
    }

    [Fact]
    public async Task Saved_and_runtime_announcements_follow_each_visible_state_once()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter();
        var page = fixture.CreatePage(presenter);
        var pending = page.PresentAsync(fixture.Request, _ => true);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

        page.SimulateAppearingForTest();
        page.SimulateAppearingForTest();
        Assert.Equal(1, page.AnnouncementCountForTest);
        Assert.Equal(WorkoutResources.English.EffortSetSaved,
            page.LastAnnouncementForTest);

        page.AnnounceStateForTest(SetEffortPromptState.Asking);
        page.AnnounceStateForTest(SetEffortPromptState.SavingEffort);
        Assert.Equal(1, page.AnnouncementCountForTest);

        page.AnnounceStateForTest(SetEffortPromptState.NeedsIncrement);
        page.AnnounceStateForTest(SetEffortPromptState.NeedsIncrement);
        Assert.Equal(2, page.AnnouncementCountForTest);
        Assert.Equal(WorkoutResources.English.GuidanceIncrementTitle,
            page.LastAnnouncementForTest);

        page.AnnounceStateForTest(SetEffortPromptState.SaveFailed);
        page.AnnounceStateForTest(SetEffortPromptState.SaveFailed);
        Assert.Equal(3, page.AnnouncementCountForTest);
        Assert.Equal(WorkoutResources.English.EffortSaveFailed,
            page.LastAnnouncementForTest);

        page.AnnounceStateForTest(SetEffortPromptState.Unavailable);
        page.AnnounceStateForTest(SetEffortPromptState.Unavailable);
        Assert.Equal(4, page.AnnouncementCountForTest);
        Assert.Equal(WorkoutResources.English.EffortUnavailable,
            page.LastAnnouncementForTest);

        await page.ViewModelForTest.ChooseEffortAsync(SetEffortRating.Productive);
        page.AnnounceCurrentStateForTest();
        page.AnnounceCurrentStateForTest();
        Assert.Equal(SetEffortPromptState.Recommendation,
            page.ViewModelForTest.State);
        Assert.Equal(5, page.AnnouncementCountForTest);
        Assert.Equal("Keep 70 kg", page.LastAnnouncementForTest);

        await page.DismissAsyncForTest();
        await pending;
    }

    [Fact]
    public async Task Appearing_requests_semantic_focus_for_heading_and_records_success()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        var presenter = new RecordingSheetPresenter();
        VisualElement? focusedTarget = null;
        var focusCalls = 0;
        var page = fixture.CreatePage(presenter, target =>
        {
            focusedTarget = target;
            focusCalls++;
            return true;
        });
        var pending = page.PresentAsync(fixture.Request, _ => true);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));

        page.SimulateAppearingForTest();

        Assert.Equal(1, focusCalls);
        Assert.Same(page.FindByName<Label>("EffortSheetHeading"), focusedTarget);
        Assert.True(page.LastSemanticFocusResultForTest);
        await page.DismissAsyncForTest();
        await pending;
    }

    [Fact]
    public async Task Retry_failure_announces_each_new_save_failed_transition()
    {
        await using var fixture = await SheetFixture.CreateAsync();
        fixture.Recorder.RecordFailuresRemaining = 2;
        var presenter = new RecordingSheetPresenter();
        var page = fixture.CreatePage(presenter);
        var pending = page.PresentAsync(fixture.Request, _ => true);
        await presenter.Presented.Task.WaitAsync(TimeSpan.FromSeconds(1));
        page.SimulateAppearingForTest();

        await page.ViewModelForTest.ChooseEffortAsync(SetEffortRating.Productive);
        Assert.Equal(SetEffortPromptState.SaveFailed, page.ViewModelForTest.State);
        Assert.Equal(2, page.AnnouncementCountForTest);

        await page.ViewModelForTest.RetryAsync();

        Assert.Equal(SetEffortPromptState.SaveFailed, page.ViewModelForTest.State);
        Assert.Equal(3, page.AnnouncementCountForTest);
        Assert.Equal(WorkoutResources.English.EffortSaveFailed,
            page.LastAnnouncementForTest);
        Assert.Equal(2, fixture.Recorder.RecordCalls);
        await page.DismissAsyncForTest();
        await pending;
    }

    private sealed class RecordingSheetPresenter(bool delayFirstDismissal = false)
        : INativeSheetPresenter
    {
        public TaskCompletionSource Presented { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondPresented { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DismissStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDisappearing { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDismissal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ContentPage? Page { get; private set; }
        public NativeSheetDetent? Detent { get; private set; }
        public int ShowCount { get; private set; }
        public int DismissCount { get; private set; }

        public Task ShowAsync(ContentPage page, NativeSheetDetent detent,
            CancellationToken cancellationToken = default)
        {
            Page = page;
            Detent = detent;
            ShowCount++;
            Presented.TrySetResult();
            if (ShowCount == 2) SecondPresented.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task DismissAsync(ContentPage page,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(Page, page);
            DismissCount++;
            if (!delayFirstDismissal || DismissCount != 1) return;
            DismissStarted.TrySetResult();
            await AllowDisappearing.Task;
            Assert.IsType<SetEffortSheetPage>(page).SimulateDisappearingForTest();
            await ReleaseDismissal.Task;
        }
    }

    private sealed class SheetFixture : IAsyncDisposable
    {
        private SheetFixture(
            SetEffortPromptRequest request,
            SheetRecorder recorder,
            IExerciseGuidancePreferenceStore preferences,
            IWeightUnitPreference unitPreference,
            AccountSessionBoundary boundary,
            IDispatcherProvider originalDispatcher)
        {
            Request = request;
            Recorder = recorder;
            Preferences = preferences;
            UnitPreference = unitPreference;
            Boundary = boundary;
            OriginalDispatcher = originalDispatcher;
        }

        public SetEffortPromptRequest Request { get; }
        public SheetRecorder Recorder { get; }
        public IExerciseGuidancePreferenceStore Preferences { get; }
        public IWeightUnitPreference UnitPreference { get; }
        public AccountSessionBoundary Boundary { get; }
        private IDispatcherProvider OriginalDispatcher { get; }

        public static Task<SheetFixture> CreateAsync()
        {
            var workoutId = Guid.NewGuid();
            var exerciseDefinitionId = Guid.NewGuid();
            var workoutExerciseId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 8, 23, 9, 0, 0, TimeSpan.Zero);
            var saved = new LocalSet(
                Guid.NewGuid(), workoutExerciseId, 0, 70m, null, 10,
                now, null, null, 1, 0, Guid.NewGuid(), null);
            var graph = new LocalWorkout(
                workoutId, LocalWorkoutStatus.Active, now.AddMinutes(-5),
                null, null, 2, 1,
                [new LocalWorkoutExercise(
                    workoutExerciseId, workoutId, exerciseDefinitionId,
                    TrackingMode.Weighted, 0, null, 2, 1, [saved])]);
            var raw = new SheetPreferenceStore();
            var originalDispatcher = DispatcherProvider.Current;
            DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
            return Task.FromResult(new SheetFixture(
                new SetEffortPromptRequest(
                    exerciseDefinitionId, TrackingMode.Weighted, saved,
                    null, Guid.NewGuid()),
                new SheetRecorder(graph),
                new ExerciseGuidancePreferenceStore(raw),
                new WeightUnitPreference(raw),
                new AccountSessionBoundary(),
                originalDispatcher));
        }

        public SetEffortSheetPage CreatePage(INativeSheetPresenter presenter) => new(
            presenter,
            new SetEffortPromptViewModel(
                Recorder, Preferences, UnitPreference, Boundary, WorkoutResources.English));

        public SetEffortSheetPage CreatePage(
            INativeSheetPresenter presenter,
            Func<VisualElement, bool> semanticFocus) => new(
                presenter,
                new SetEffortPromptViewModel(
                    Recorder,
                    Preferences,
                    UnitPreference,
                    Boundary,
                    WorkoutResources.English),
                semanticFocus);

        public ValueTask DisposeAsync()
        {
            DispatcherProvider.SetCurrent(OriginalDispatcher);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SheetRecorder(LocalWorkout initialGraph) : ISetEffortRecorder
    {
        private LocalWorkout _graph = initialGraph;
        public int RecordCalls { get; private set; }
        public int RecordFailuresRemaining { get; set; }

        public Task<LocalSet> RecordSetEffortAsync(
            Guid exerciseDefinitionId,
            Guid setId,
            SetEffortRating effort,
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecordCalls++;
            if (RecordFailuresRemaining > 0)
            {
                RecordFailuresRemaining--;
                throw new InvalidOperationException("Effort write failed.");
            }
            var exercise = _graph.Exercises.Single(item =>
                item.DeletedAt is null
                && item.ExerciseDefinitionId == exerciseDefinitionId);
            var saved = exercise.Sets.Single(item =>
                item.DeletedAt is null && item.Id == setId);
            var updated = saved with
            {
                Effort = effort,
                UpdatedAt = saved.CompletedAt.AddMinutes(1),
                Version = saved.Version + 1
            };
            var updatedExercise = exercise with
            {
                Sets = exercise.Sets
                    .Select(item => item.Id == setId ? updated : item)
                    .ToArray(),
                Version = exercise.Version + 1
            };
            _graph = _graph with
            {
                Exercises = _graph.Exercises
                    .Select(item => item.Id == exercise.Id ? updatedExercise : item)
                    .ToArray(),
                Version = _graph.Version + 1
            };
            return Task.FromResult(updated);
        }

        public Task<LocalWorkout?> RestoreActiveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalWorkout?>(_graph);
    }

    private sealed class SheetPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
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
