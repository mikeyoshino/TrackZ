using System.ComponentModel;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetEffortSheetPage : ContentPage, ISetEffortSheet
{
    private readonly INativeSheetPresenter _presenter;
    private readonly SetEffortPromptViewModel _viewModel;
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private TaskCompletionSource? _dismissed;
    private int _dismissStarted;
    private bool _savedAnnouncementMade;
    private SetEffortPromptState? _announcedState;

    public SetEffortSheetPage(
        INativeSheetPresenter presenter,
        SetEffortPromptViewModel viewModel)
    {
        _presenter = presenter;
        _viewModel = viewModel;
        InitializeComponent();
    }

    internal string? LastAnnouncementForTest { get; private set; }
    internal int AnnouncementCountForTest { get; private set; }
    internal int FocusAttemptCountForTest { get; private set; }
    internal SetEffortPromptViewModel ViewModelForTest => _viewModel;

    public async Task PresentAsync(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(applyToDraft);
        await _presentationGate.WaitAsync(cancellationToken);
        try
        {
            if (_dismissed is not null)
                throw new InvalidOperationException(
                    "The effort sheet presentation gate is inconsistent.");
            _dismissed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _dismissStarted, 0);
            _savedAnnouncementMade = false;
            _announcedState = null;
            LastAnnouncementForTest = null;
            AnnouncementCountForTest = 0;
            FocusAttemptCountForTest = 0;
            try
            {
                _viewModel.Initialize(request, applyToDraft);
                _viewModel.DismissRequested += OnDismissRequested;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                BindingContext = _viewModel;
                await _presenter.ShowAsync(
                    this, NativeSheetDetent.Medium, cancellationToken);
                await _dismissed.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await DismissCoreAsync();
                }
                catch
                {
                    _dismissed?.TrySetResult();
                }
                throw;
            }
            finally
            {
                CleanupPresentation();
            }
        }
        finally
        {
            _presentationGate.Release();
        }
    }

    private async Task DismissCoreAsync()
    {
        if (Interlocked.Exchange(ref _dismissStarted, 1) != 0) return;
        try
        {
            await _presenter.DismissAsync(this, CancellationToken.None);
        }
        finally
        {
            _dismissed?.TrySetResult();
        }
    }

    private async void OnDismissRequested(object? sender, EventArgs eventArgs)
    {
        try
        {
            await DismissCoreAsync();
        }
        catch
        {
            _dismissed?.TrySetResult();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not nameof(SetEffortPromptViewModel.State)
            and not null)
            return;
        DispatchBestEffort(AnnounceCurrentState);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        DispatchBestEffort(RunAppearingActions);
    }

    protected override void OnDisappearing()
    {
        Interlocked.Exchange(ref _dismissStarted, 1);
        _dismissed?.TrySetResult();
        base.OnDisappearing();
    }

    private void RunAppearingActions()
    {
        FocusAttemptCountForTest++;
        try { EffortSheetHeading.Focus(); }
        catch { }
        if (_savedAnnouncementMade) return;
        _savedAnnouncementMade = true;
        AnnounceBestEffort(_viewModel.Text.EffortSetSaved);
    }

    private void AnnounceCurrentState() => AnnounceState(_viewModel.State);

    private void AnnounceState(SetEffortPromptState state)
    {
        if (_announcedState == state) return;
        var announcement = state switch
        {
            SetEffortPromptState.NeedsIncrement =>
                _viewModel.Text.GuidanceIncrementTitle,
            SetEffortPromptState.Recommendation =>
                _viewModel.RecommendationTitle,
            SetEffortPromptState.SaveFailed =>
                _viewModel.Text.EffortSaveFailed,
            SetEffortPromptState.Unavailable =>
                _viewModel.Text.EffortUnavailable,
            _ => null
        };
        if (announcement is null) return;
        _announcedState = state;
        AnnounceBestEffort(announcement);
    }

    private void AnnounceBestEffort(string announcement)
    {
        LastAnnouncementForTest = announcement;
        AnnouncementCountForTest++;
        try { SemanticScreenReader.Default.Announce(announcement); }
        catch { }
    }

    private void DispatchBestEffort(Action action)
    {
        try { Dispatcher.Dispatch(action); }
        catch
        {
            try { action(); }
            catch { }
        }
    }

    private void CleanupPresentation()
    {
        _viewModel.DismissRequested -= OnDismissRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Deactivate();
        BindingContext = null;
        Interlocked.Exchange(ref _dismissed, null);
        Interlocked.Exchange(ref _dismissStarted, 0);
    }

    internal Task DismissAsyncForTest() => DismissCoreAsync();
    internal void SimulateAppearingForTest() => RunAppearingActions();
    internal void SimulateDisappearingForTest() => OnDisappearing();
    internal void AnnounceCurrentStateForTest() => AnnounceCurrentState();
    internal void AnnounceStateForTest(SetEffortPromptState state) =>
        AnnounceState(state);
}
