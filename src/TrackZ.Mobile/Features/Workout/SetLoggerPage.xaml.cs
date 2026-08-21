using System.ComponentModel;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetLoggerPage : ContentPage, IQueryAttributable
{
    private readonly SetLoggerViewModel _viewModel;
    private readonly MauiSetSavedFeedback _feedback;
    private readonly ISetSavedPulseDriver _pulse;
    private readonly IInlineSetEditorTransition _inlineTransition;
    private bool _wasParented;
    private bool _deactivated;
    private bool _feedbackSubscribed;
    private bool _draftTransitionSubscribed;
    private bool _draftWasVisible;
    private bool _restoreAddFocusWhenReady;
    private CancellationTokenSource? _draftTransitionCancellation;

    public SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        Presentation.ITrackZMotion motion,
        IInlineSetEditorTransition inlineTransition)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        _inlineTransition = inlineTransition;
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
            new MauiInlineSetEditorTransition())
    {
    }

    protected SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver pulse,
        IInlineSetEditorTransition inlineTransition)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        _pulse = pulse;
        _inlineTransition = inlineTransition;
        InitializeComponent();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SubscribeFeedback();
        SubscribeDraftTransition();
    }

    protected override void OnDisappearing()
    {
        UnsubscribeFeedback();
        UnsubscribeDraftTransition();
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
        if (_deactivated) return;
        _deactivated = true;
        UnsubscribeFeedback();
        UnsubscribeDraftTransition();
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
}
