namespace TrackZ.Mobile.Features.Workout;

public partial class SetLoggerPage : ContentPage, IQueryAttributable
{
    private readonly SetLoggerViewModel _viewModel;
    private readonly MauiSetSavedFeedback _feedback;
    private readonly ISetSavedPulseDriver _pulse;
    private readonly SetEntrySheetPage? _entrySheet;
    private bool _wasParented;
    private bool _deactivated;
    private bool _feedbackSubscribed;

    public SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        SetEntrySheetPage entrySheet,
        Presentation.ITrackZMotion motion)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        _entrySheet = entrySheet;
        InitializeComponent();
        _pulse = new MauiSetSavedPulseDriver(SavedPulse, motion: motion);
        BindingContext = _viewModel;
    }

    protected SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver pulse)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        _pulse = pulse;
        InitializeComponent();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SubscribeFeedback();
    }

    protected override void OnDisappearing()
    {
        UnsubscribeFeedback();
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

    private async void OnLogNextSetClicked(object? sender, EventArgs eventArgs)
    {
        if (_entrySheet is null) return;
        UnsubscribeFeedback();
        try
        {
            await _entrySheet.PresentAsync(_viewModel);
        }
        finally
        {
            SubscribeFeedback();
        }
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
