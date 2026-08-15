using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed class MauiSetSavedFeedback(Func<bool>? hapticsEnabled = null) : ISetSavedFeedback
{
    private readonly Func<bool> _hapticsEnabled = hapticsEnabled
        ?? (() => Preferences.Default.Get("trackz_haptics_enabled", true));

    public event Func<LocalSet, CancellationToken, Task>? Saved;

    public async Task SetSavedAsync(LocalSet savedSet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_hapticsEnabled())
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                HapticFeedback.Default.Perform(HapticFeedbackType.Click));
            cancellationToken.ThrowIfCancellationRequested();
        }

        var handlers = Saved;
        if (handlers is null) return;
        foreach (Func<LocalSet, CancellationToken, Task> handler in handlers.GetInvocationList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler(savedSet, cancellationToken);
        }
    }
}
