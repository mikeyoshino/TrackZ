using System.ComponentModel;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetLoggerPage : ContentPage, IQueryAttributable
{
    private readonly SetLoggerViewModel _viewModel;
    private readonly MauiSetSavedFeedback _feedback;
    private readonly ISetSavedPulseDriver _pulse;
    private readonly IInlineSetEditorTransition _inlineTransition;
    private readonly ISetEffortSheet _effortSheet;
    private readonly IAccountSessionBoundary _sessionBoundary;
    private readonly object _effortSheetLifetimeSync = new();
    private bool _wasParented;
    private volatile bool _deactivated;
    private bool _feedbackSubscribed;
    private bool _draftTransitionSubscribed;
    private bool _effortPromptSubscribed;
    private bool _draftWasVisible;
    private bool _restoreAddFocusWhenReady;
    private CancellationTokenSource? _draftTransitionCancellation;
    private EffortSheetLifetime? _effortSheetLifetime;
    private CancellationTokenSource? _coachTargetLifetime;

    public SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        Presentation.ITrackZMotion motion,
        IInlineSetEditorTransition inlineTransition,
        ISetEffortSheet effortSheet,
        IAccountSessionBoundary sessionBoundary)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        _inlineTransition = inlineTransition;
        _effortSheet = effortSheet;
        _sessionBoundary = sessionBoundary;
        InitializeComponent();
        _pulse = new MauiSetSavedPulseDriver(SavedPulse, motion: motion);
        BindingContext = _viewModel;
    }

    protected SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver pulse) : this(
            viewModel,
            feedback,
            pulse,
            new MauiInlineSetEditorTransition(),
            NullSetEffortSheet.Instance,
            new AccountSessionBoundary())
    {
    }

    protected SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver pulse,
        IInlineSetEditorTransition inlineTransition) : this(
            viewModel,
            feedback,
            pulse,
            inlineTransition,
            NullSetEffortSheet.Instance,
            new AccountSessionBoundary())
    {
    }

    protected SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver pulse,
        IInlineSetEditorTransition inlineTransition,
        ISetEffortSheet effortSheet,
        IAccountSessionBoundary sessionBoundary)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        _pulse = pulse;
        _inlineTransition = inlineTransition;
        _effortSheet = effortSheet;
        _sessionBoundary = sessionBoundary;
        InitializeComponent();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_deactivated) return;
        _sessionBoundary.SessionReset -= OnCoachSessionReset;
        _sessionBoundary.SessionReset += OnCoachSessionReset;
        RefreshWeightUnitButtons();
        FinishExerciseButton.Text = CoachCopy.T("จบท่านี้ · เช็กการฝึก", "Finish exercise · quick check-in");
        WarmupSetLabel.Text = CoachCopy.T("เซ็ตวอร์มอัป · ไม่รวมในรายงาน", "Warm-up set · excluded from report");
        SemanticProperties.SetDescription(WarmupSetSwitch, WarmupSetLabel.Text);
        SubscribeFeedback();
        SubscribeDraftTransition();
        SubscribeEffortPrompt();
        _coachTargetLifetime?.Cancel();
        _coachTargetLifetime?.Dispose();
        _coachTargetLifetime = new CancellationTokenSource();
        await LoadCoachTargetAsync(_coachTargetLifetime.Token);
    }

    private async Task LoadCoachTargetAsync(CancellationToken token)
    {
        CoachTargetHost.Children.Clear(); CoachTargetHost.IsVisible = false;
        if (Handler?.MauiContext?.Services is not { } services) return;
        var generation = _sessionBoundary.Capture();
        try
        {
            using var lease = _sessionBoundary.CreateCancellationLease(generation, token);
            var report = await services.GetRequiredService<TrainingCoachSource>().LoadAsync(lease.Token);
            var exercise = report.Exercises.FirstOrDefault(e => e.Id == _viewModel.ExerciseDefinitionId && e.Accepted && e.Recommendation.IsIncrease);
            if (exercise is null) return;
            await _sessionBoundary.TryCommitAsync(generation, _ =>
            {
                CoachTargetHost.Children.Add(CoachUi.Card(CoachUi.Stack(
                    CoachUi.Label(CoachCopy.T("เป้าหมายที่คุณเลือกไว้", "Your chosen target"), 14, true),
                    CoachUi.Label(CoachCopy.Target(exercise, services.GetRequiredService<IWeightUnitPreference>().Current), 21),
                    CoachUi.Label(CoachCopy.T("ถ้ายังคุมท่าได้ · ไม่เปลี่ยนน้ำหนักให้อัตโนมัติ", "Only with good control · load is not changed automatically"), 12, true))));
                CoachTargetHost.IsVisible = true;
                return Task.CompletedTask;
            }, lease.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Optional target must never prevent set logging. */ }
    }

    private async void OnFinishExerciseClicked(object? sender, EventArgs args)
    {
        if (_deactivated || !_viewModel.HasTodaySets || Handler?.MauiContext?.Services is not { } services) return;
        FinishExerciseButton.IsEnabled = false;
        try
        {
            var page = new ExerciseCheckInPage(services.GetRequiredService<TrainingCoachSource>(),
                services.GetRequiredService<CoachJournal>(), _sessionBoundary,
                services.GetRequiredService<IWeightUnitPreference>(), services.GetRequiredService<IExerciseGuidancePreferenceStore>(),
                services.GetRequiredService<IClock>(), _viewModel.ExerciseDefinitionId);
            await Navigation.PushAsync(page, false);
        }
        finally { if (!_deactivated) FinishExerciseButton.IsEnabled = true; }
    }

    protected override void OnDisappearing()
    {
        _sessionBoundary.SessionReset -= OnCoachSessionReset;
        ClearCoachTarget();
        UnsubscribeFeedback();
        UnsubscribeDraftTransition();
        UnsubscribeEffortPrompt();
        base.OnDisappearing();
    }

    private void OnCoachSessionReset(object? sender, EventArgs args) => ClearCoachTarget();

    private void ClearCoachTarget()
    {
        _coachTargetLifetime?.Cancel();
        void Clear()
        {
            CoachTargetHost.Children.Clear();
            CoachTargetHost.IsVisible = false;
            WarmupSetSwitch.IsToggled = false;
        }
        if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(Clear);
        else Clear();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is not null)
        {
            _wasParented = true;
            return;
        }
        if (_wasParented) Deactivate();
    }

    public void Deactivate()
    {
        EffortSheetLifetime? lifetime;
        lock (_effortSheetLifetimeSync)
        {
            if (_deactivated) return;
            _deactivated = true;
            lifetime = _effortSheetLifetime;
            _effortSheetLifetime = null;
        }
        _sessionBoundary.SessionReset -= OnCoachSessionReset;
        ClearCoachTarget();
        UnsubscribeFeedback();
        UnsubscribeDraftTransition();
        UnsubscribeEffortPrompt();
        DisposeEffortSheetLifetimeBestEffort(lifetime);
        _viewModel.Deactivate();
        BindingContext = null;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("exerciseId", out var rawId)
            || !Guid.TryParse(rawId?.ToString(), out var exerciseId)
            || exerciseId == Guid.Empty
            || !query.TryGetValue("name", out var rawName)
            || string.IsNullOrWhiteSpace(rawName?.ToString())) return;
        await _viewModel.LoadAsync(exerciseId, Uri.UnescapeDataString(rawName.ToString()!));
    }

    private async Task OnSetSavedAsync(SetSavedPresentation presentation, SetSavedFeedbackSession session)
    {
        var warmup = WarmupSetSwitch.IsToggled;
        if (Handler?.MauiContext?.Services.GetService<CoachJournal>() is { } journal)
        {
            var generation = _sessionBoundary.Capture();
            try
            {
                // If optional classification fails, the set remains explicitly
                // unclassified and can be labelled in the exercise check-in.
                await _sessionBoundary.TryCommitAsync(generation,
                    token => journal.SetWarmupAsync(presentation.Set.Id, warmup, token), session.CancellationToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { }
        }
        WarmupSetSwitch.IsToggled = false;
        SavedPrimary.Text = presentation.PrimaryText;
        SavedSecondary.Text = presentation.SecondaryText;
        await RunSavedAnimationAsync(presentation, session);
    }

    private void SubscribeFeedback()
    {
        if (_deactivated || _feedbackSubscribed) return;
        _feedback.Saved += OnSetSavedAsync;
        _feedbackSubscribed = true;
    }

    private void UnsubscribeFeedback()
    {
        if (!_feedbackSubscribed) return;
        _feedback.Saved -= OnSetSavedAsync;
        _feedbackSubscribed = false;
    }

    private void SubscribeEffortPrompt()
    {
        if (_deactivated || _effortPromptSubscribed) return;
        _viewModel.EffortPromptRequested += OnEffortPromptRequested;
        _effortPromptSubscribed = true;
    }

    private void UnsubscribeEffortPrompt()
    {
        if (!_effortPromptSubscribed) return;
        _viewModel.EffortPromptRequested -= OnEffortPromptRequested;
        _effortPromptSubscribed = false;
    }

    private void OnEffortPromptRequested(
        object? sender,
        SetEffortPromptRequestedEventArgs eventArgs)
    {
        _ = PresentEffortPromptBestEffortAsync(eventArgs);
    }

    private async Task PresentEffortPromptBestEffortAsync(
        SetEffortPromptRequestedEventArgs eventArgs)
    {
        EffortSheetLifetime? lifetime = null;
        EffortSheetLifetime? previous = null;
        var published = false;
        try
        {
            if (_deactivated) return;
            lifetime = new EffortSheetLifetime(_sessionBoundary);
            lock (_effortSheetLifetimeSync)
            {
                if (_deactivated) return;
                previous = _effortSheetLifetime;
                _effortSheetLifetime = lifetime;
                published = true;
            }
            DisposeEffortSheetLifetimeBestEffort(previous);
            await _effortSheet.PresentAsync(
                eventArgs.Request,
                _viewModel.TryApplyGuidanceToNextDraft,
                lifetime.Token);
        }
        catch (OperationCanceledException)
            when (lifetime?.Token.IsCancellationRequested == true) { }
        catch
        {
            // The set is already durable; modal presentation is best effort.
        }
        finally
        {
            if (published)
            {
                lock (_effortSheetLifetimeSync)
                {
                    if (ReferenceEquals(_effortSheetLifetime, lifetime))
                        _effortSheetLifetime = null;
                }
            }
            DisposeEffortSheetLifetimeBestEffort(lifetime);
        }
    }

    private static void DisposeEffortSheetLifetimeBestEffort(
        EffortSheetLifetime? lifetime)
    {
        try { lifetime?.Dispose(); }
        catch
        {
            // Cancellation and lease cleanup cannot undo the durable set.
        }
    }

    private void SubscribeDraftTransition()
    {
        if (_deactivated || _draftTransitionSubscribed) return;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _draftTransitionSubscribed = true;
        if (_viewModel.HasDraftSet) RevealDraftEditor();
    }

    private void UnsubscribeDraftTransition()
    {
        CancelDraftTransition();
        _restoreAddFocusWhenReady = false;
        if (!_draftTransitionSubscribed) return;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _draftTransitionSubscribed = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_deactivated) return;
        if (eventArgs.PropertyName == nameof(SetLoggerViewModel.DisplayUnit))
        {
            RefreshWeightUnitButtons();
            return;
        }
        if (eventArgs.PropertyName == nameof(SetLoggerViewModel.IsBusy))
        {
            if (!_viewModel.IsBusy && _restoreAddFocusWhenReady) RestoreAddFocus();
            return;
        }
        if (eventArgs.PropertyName != nameof(SetLoggerViewModel.HasDraftSet)) return;
        if (_viewModel.HasDraftSet)
        {
            _restoreAddFocusWhenReady = false;
            RevealDraftEditor();
            return;
        }
        if (!_draftWasVisible) return;
        CancelDraftTransition();
        _draftWasVisible = false;
        if (_viewModel.IsBusy)
        {
            _restoreAddFocusWhenReady = true;
            return;
        }
        RestoreAddFocus();
    }

    private void RefreshWeightUnitButtons() =>
        WeightUnitButtonPresenter.Apply(
            KilogramsButton,
            PoundsButton,
            _viewModel.DisplayUnit);

    private void RestoreAddFocus()
    {
        _restoreAddFocusWhenReady = false;
        _inlineTransition.RestoreFocus(AddSetButton);
    }

    private void RevealDraftEditor()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _draftTransitionCancellation, cancellation);
        CancelAndDispose(previous);
        _draftWasVisible = true;
        _ = RevealDraftEditorAsync(cancellation);
    }

    private async Task RevealDraftEditorAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await _inlineTransition.RevealAsync(
                SetLoggerScroll,
                InlineSetEditor,
                _viewModel.NextSetText,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            // Viewport and assistive feedback are best-effort; draft persistence stays usable.
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _draftTransitionCancellation,
                        null,
                        cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private void CancelDraftTransition()
    {
        var cancellation = Interlocked.Exchange(ref _draftTransitionCancellation, null);
        CancelAndDispose(cancellation);
    }

    private static void CancelAndDispose(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task RunSavedAnimationAsync(SetSavedPresentation presentation, SetSavedFeedbackSession session)
    {
        using var cancellation = session.CancellationToken.Register(_pulse.Cancel);
        await _pulse.InvokeAsync(async () =>
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            Task? running = null;
            if (!session.TryStartPhase(() =>
                running = _pulse.StartAsync(presentation.Outcome, session.CancellationToken))) return;
            await running!;
            session.CancellationToken.ThrowIfCancellationRequested();
        });
    }

    private sealed class NullSetEffortSheet : ISetEffortSheet
    {
        public static NullSetEffortSheet Instance { get; } = new();

        public Task PresentAsync(
            SetEffortPromptRequest request,
            Func<HypertrophyGuidanceResult, bool> applyToDraft,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EffortSheetLifetime : IDisposable
    {
        private CancellationTokenSource? _ownerCancellation;
        private AccountSessionCancellationLease? _sessionLease;

        public EffortSheetLifetime(IAccountSessionBoundary boundary)
        {
            _ownerCancellation = new CancellationTokenSource();
            _sessionLease = boundary.CreateCancellationLease(
                boundary.Capture(), _ownerCancellation.Token);
            Token = _sessionLease.Token;
        }

        public CancellationToken Token { get; }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _ownerCancellation, null);
            var lease = Interlocked.Exchange(ref _sessionLease, null);
            if (owner is null)
            {
                lease?.Dispose();
                return;
            }
            try
            {
                try { owner.Cancel(); }
                catch { /* Cancellation callbacks are best effort here. */ }
                lease?.Dispose();
            }
            finally
            {
                owner.Dispose();
            }
        }
    }
}
