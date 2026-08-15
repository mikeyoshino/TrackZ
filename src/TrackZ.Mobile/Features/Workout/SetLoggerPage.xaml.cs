using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public partial class SetLoggerPage : ContentPage, IQueryAttributable
{
    private readonly SetLoggerViewModel _viewModel;
    private readonly MauiSetSavedFeedback _feedback;

    public SetLoggerPage(SetLoggerViewModel viewModel, MauiSetSavedFeedback feedback)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _feedback = feedback;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _feedback.Saved += OnSetSavedAsync;
    }

    protected override void OnDisappearing()
    {
        _feedback.Saved -= OnSetSavedAsync;
        base.OnDisappearing();
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

    private Task OnSetSavedAsync(LocalSet savedSet) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        SavedPulse.CancelAnimations();
        SavedPulse.Opacity = 1;
        if (Preferences.Default.Get("trackz_reduce_motion", false))
        {
            await SavedPulse.FadeToAsync(0, 180, Easing.Linear);
            return;
        }
        SavedPulse.Scale = 0.97;
        await Task.WhenAll(
            SavedPulse.ScaleToAsync(1, 160, Easing.CubicOut),
            SavedPulse.FadeToAsync(0, 520, Easing.CubicIn));
    });
}
