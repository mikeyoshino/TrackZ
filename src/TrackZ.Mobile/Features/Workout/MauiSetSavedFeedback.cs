using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed class MauiSetSavedFeedback : ISetSavedFeedback
{
    public event Func<LocalSet, Task>? Saved;

    public async Task SetSavedAsync(LocalSet savedSet, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Preferences.Default.Get("trackz_haptics_enabled", true))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                HapticFeedback.Default.Perform(HapticFeedbackType.Click));
        }

        var handlers = Saved;
        if (handlers is null) return;
        foreach (Func<LocalSet, Task> handler in handlers.GetInvocationList())
            await handler(savedSet);
    }
}
