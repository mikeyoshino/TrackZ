using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetLoggerPage : ContentPage, IQueryAttributable
{
    private readonly SetLoggerViewModel _viewModel;
    private readonly MauiSetSavedFeedback _feedback;
    private readonly ISetSavedPulseDriver _pulse;
    private bool _wasParented;
    private bool _deactivated;

    public SetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver? pulse = null)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        InitializeComponent();
        _pulse = pulse ?? new MauiSetSavedPulseDriver(SavedPulse);
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_deactivated) _feedback.Saved += OnSetSavedAsync;
    }

    protected override void OnDisappearing()
    {
        _feedback.Saved -= OnSetSavedAsync;
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
        _feedback.Saved -= OnSetSavedAsync;
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

    private Task OnSetSavedAsync(LocalSet savedSet, SetSavedFeedbackSession session) =>
        AnimateSavedAsync(session);

    private async Task AnimateSavedAsync(SetSavedFeedbackSession session)
    {
        using var cancellation = session.CancellationToken.Register(_pulse.Cancel);
        await _pulse.InvokeAsync(async () =>
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            Task? running = null;
            if (!session.TryStartPhase(() =>
                running = _pulse.StartAsync(session.CancellationToken))) return;
            await running!;
            session.CancellationToken.ThrowIfCancellationRequested();
        });
    }
}
