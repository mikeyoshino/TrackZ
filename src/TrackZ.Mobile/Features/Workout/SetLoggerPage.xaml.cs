using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetLoggerPage : ContentPage, IQueryAttributable
{
    private readonly SetLoggerViewModel _viewModel;
    private readonly MauiSetSavedFeedback _feedback;
    private bool _wasParented;
    private bool _deactivated;

    public SetLoggerPage(SetLoggerViewModel viewModel, MauiSetSavedFeedback feedback)
    {
        _viewModel = viewModel;
        _feedback = feedback;
        InitializeComponent();
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

    private Task OnSetSavedAsync(LocalSet savedSet, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(() => AnimateSavedAsync(cancellationToken));

    private async Task AnimateSavedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellation = cancellationToken.Register(() =>
            MainThread.BeginInvokeOnMainThread(SavedPulse.CancelAnimations));
        SavedPulse.CancelAnimations();
        SavedPulse.Opacity = 1;
        if (Preferences.Default.Get("trackz_reduce_motion", false))
        {
            await SavedPulse.FadeToAsync(0, 180, Easing.Linear);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }
        SavedPulse.Scale = 0.97;
        await Task.WhenAll(
            SavedPulse.ScaleToAsync(1, 160, Easing.CubicOut),
            SavedPulse.FadeToAsync(0, 520, Easing.CubicIn));
        cancellationToken.ThrowIfCancellationRequested();
    }
}
