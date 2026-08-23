using System.ComponentModel;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetEffortSheetPage : ContentPage, ISetEffortSheet
{
    private readonly INativeSheetPresenter _presenter;
    private readonly SetEffortPromptViewModel _viewModel;
    private readonly Func<VisualElement, bool> _semanticFocus;
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private readonly object _dismissalSync = new();
    private TaskCompletionSource? _dismissed;
    private Task? _dismissalTask;
    private bool _savedAnnouncementMade;
    private SetEffortPromptState? _announcedState;

    public SetEffortSheetPage(
        INativeSheetPresenter presenter,
        SetEffortPromptViewModel viewModel) : this(
            presenter,
            viewModel,
            SemanticAccessibilityFocus.TrySetFocus)
    {
    }

    internal SetEffortSheetPage(
        INativeSheetPresenter presenter,
        SetEffortPromptViewModel viewModel,
        Func<VisualElement, bool> semanticFocus)
    {
        _presenter = presenter;
        _viewModel = viewModel;
        _semanticFocus = semanticFocus
            ?? throw new ArgumentNullException(nameof(semanticFocus));
        InitializeComponent();
    }

    internal string? LastAnnouncementForTest { get; private set; }
    internal int AnnouncementCountForTest { get; private set; }
    internal bool LastSemanticFocusResultForTest { get; private set; }
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
            lock (_dismissalSync) _dismissalTask = null;
            _savedAnnouncementMade = false;
            _announcedState = null;
            LastAnnouncementForTest = null;
            AnnouncementCountForTest = 0;
            LastSemanticFocusResultForTest = false;
            try
            {
                _viewModel.Initialize(request, applyToDraft);
                _viewModel.DismissRequested += OnDismissRequested;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                BindingContext = _viewModel;
                await _presenter.ShowAsync(
                    this, NativeSheetDetent.Medium, cancellationToken);
                await _dismissed.Task.WaitAsync(cancellationToken);
                await AwaitDismissalCompletionAsync();
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

    private Task DismissCoreAsync()
    {
        lock (_dismissalSync)
            return _dismissalTask ??= DismissNativeAsync();
    }

    private async Task DismissNativeAsync()
    {
        try
        {
            await _presenter.DismissAsync(this, CancellationToken.None);
        }
        finally
        {
            _dismissed?.TrySetResult();
        }
    }

    private async Task AwaitDismissalCompletionAsync()
    {
        Task? dismissal;
        lock (_dismissalSync) dismissal = _dismissalTask;
        if (dismissal is null) return;
        try { await dismissal; }
        catch
        {
            // Dismissal is best effort, but replacement still waits for it to finish.
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
        lock (_dismissalSync)
            _dismissalTask ??= Task.CompletedTask;
        _dismissed?.TrySetResult();
        base.OnDisappearing();
    }

    private void RunAppearingActions()
    {
        try
        {
            LastSemanticFocusResultForTest =
                _semanticFocus(EffortSheetHeading);
        }
        catch
        {
            LastSemanticFocusResultForTest = false;
        }
        if (_savedAnnouncementMade) return;
        _savedAnnouncementMade = true;
        AnnounceBestEffort(_viewModel.Text.EffortSetSaved);
    }

    private void AnnounceCurrentState() => AnnounceState(_viewModel.State);

    private void AnnounceState(SetEffortPromptState state)
    {
        if (_announcedState == state) return;
        _announcedState = state;
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
        lock (_dismissalSync) _dismissalTask = null;
    }

    internal Task DismissAsyncForTest() => DismissCoreAsync();
    internal void SimulateAppearingForTest() => RunAppearingActions();
    internal void SimulateDisappearingForTest() => OnDisappearing();
    internal void AnnounceCurrentStateForTest() => AnnounceCurrentState();
    internal void AnnounceStateForTest(SetEffortPromptState state) =>
        AnnounceState(state);
}

internal static class SemanticAccessibilityFocus
{
    internal static bool TrySetFocus(VisualElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
#if IOS || MACCATALYST
        if (target.Handler?.PlatformView is UIKit.UIView nativeView)
        {
            UIKit.UIAccessibility.PostNotification(
                UIKit.UIAccessibilityPostNotification.ScreenChanged,
                nativeView);
            return true;
        }
#elif ANDROID
        if (target.Handler?.PlatformView is Android.Views.View nativeView)
        {
            nativeView.SendAccessibilityEvent(
                Android.Views.Accessibility.EventTypes.ViewAccessibilityFocused);
            return true;
        }
#endif
        return target.Focus();
    }
}
