namespace TrackZ.Mobile.Features.Workout;

public interface ISetSavedPulseDriver
{
    Task InvokeAsync(Func<Task> action);
    Task StartAsync(CancellationToken cancellationToken);
    void Cancel();
}

public sealed class MauiSetSavedPulseDriver(VisualElement pulse) : ISetSavedPulseDriver
{
    public Task InvokeAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        pulse.CancelAnimations();
        pulse.Opacity = 1;
        if (Preferences.Default.Get("trackz_reduce_motion", false))
        {
            await pulse.FadeToAsync(0, 180, Easing.Linear);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }
        pulse.Scale = 0.97;
        await Task.WhenAll(
            pulse.ScaleToAsync(1, 160, Easing.CubicOut),
            pulse.FadeToAsync(0, 520, Easing.CubicIn));
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Cancel() => MainThread.BeginInvokeOnMainThread(pulse.CancelAnimations);
}
