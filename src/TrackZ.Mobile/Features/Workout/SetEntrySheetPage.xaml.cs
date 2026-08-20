using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetEntrySheetPage : ContentPage
{
    private readonly INativeSheetPresenter _presenter;
    private readonly MauiSetSavedFeedback _feedback;
    private readonly ITrackZMotion _motion;
    private SetLoggerViewModel? _viewModel;
    private bool _subscribed;
    private TaskCompletionSource? _dismissed;

    public SetEntrySheetPage(
        INativeSheetPresenter presenter,
        MauiSetSavedFeedback feedback,
        ITrackZMotion motion)
    {
        _presenter = presenter;
        _feedback = feedback;
        _motion = motion;
        InitializeComponent();
    }

    public async Task PresentAsync(
        SetLoggerViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        BindingContext = viewModel;
        _dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Subscribe();
        await _presenter.ShowAsync(this, NativeSheetDetent.Large, cancellationToken);
        await _dismissed.Task.WaitAsync(cancellationToken);
    }

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        if (_viewModel is null) return;
        var count = _viewModel.TodaySets.Count;
        await _viewModel.CompleteSetCommand.ExecuteAsync();
        if (_viewModel.TodaySets.Count > count)
            await DismissAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs eventArgs) =>
        await DismissAsync();

    private async Task DismissAsync()
    {
        Unsubscribe();
        _motion.Cancel(RewardOverlay);
        await _presenter.DismissAsync(this);
        _dismissed?.TrySetResult();
        BindingContext = null;
        _viewModel = null;
    }

    protected override void OnDisappearing()
    {
        Unsubscribe();
        _motion.Cancel(RewardOverlay);
        _dismissed?.TrySetResult();
        base.OnDisappearing();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _feedback.Saved += OnSetSavedAsync;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _feedback.Saved -= OnSetSavedAsync;
        _subscribed = false;
    }

    private async Task OnSetSavedAsync(
        SetSavedPresentation presentation,
        SetSavedFeedbackSession session)
    {
        RewardPrimary.Text = presentation.PrimaryText;
        RewardSecondary.Text = presentation.SecondaryText;
        using var cancellation = session.CancellationToken.Register(() => _motion.Cancel(RewardOverlay));
        Task? running = null;
        if (!session.TryStartPhase(() => running = _motion.PlaySetSavedAsync(
                RewardOverlay,
                presentation.Outcome,
                session.CancellationToken))) return;
        await running!;
        session.CancellationToken.ThrowIfCancellationRequested();
    }
}
