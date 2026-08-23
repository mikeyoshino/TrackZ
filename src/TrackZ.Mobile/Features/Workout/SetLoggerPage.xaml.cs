using System.ComponentModel;
using TrackZ.Mobile.Identity;

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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SubscribeFeedback();
        SubscribeDraftTransition();
        SubscribeEffortPrompt();
    }

    protected override void OnDisappearing()
    {
        UnsubscribeFeedback();
        UnsubscribeDraftTransition();
        UnsubscribeEffortPrompt();
        base.OnDisappearing();
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

    private Task OnSetSavedAsync(SetSavedPresentation presentation, SetSavedFeedbackSession session)
    {
        SavedPrimary.Text = presentation.PrimaryText;
        SavedSecondary.Text = presentation.SecondaryText;
        return RunSavedAnimationAsync(presentation, session);
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
