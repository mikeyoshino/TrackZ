using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed class MauiSetSavedFeedback(
    Func<bool>? hapticsEnabled = null,
    Func<Action, Task>? invokeOnMainThread = null,
    Action? performHaptic = null) : ISetSavedFeedback
{
    private readonly Func<bool> _hapticsEnabled = hapticsEnabled
        ?? (() => Preferences.Default.Get("trackz_haptics_enabled", true));
    private readonly Func<Action, Task> _invokeOnMainThread = invokeOnMainThread
        ?? MainThread.InvokeOnMainThreadAsync;
    private readonly Action _performHaptic = performHaptic
        ?? (() => HapticFeedback.Default.Perform(HapticFeedbackType.Click));

    public event Func<LocalSet, SetSavedFeedbackSession, Task>? Saved;

    public async Task SetSavedAsync(LocalSet savedSet, SetSavedFeedbackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.CancellationToken.ThrowIfCancellationRequested();
        if (_hapticsEnabled())
        {
            var started = false;
            await _invokeOnMainThread(() =>
                started = session.TryStartPhase(_performHaptic));
            if (!started) return;
            session.CancellationToken.ThrowIfCancellationRequested();
        }

        var handlers = Saved;
        if (handlers is null) return;
        foreach (Func<LocalSet, SetSavedFeedbackSession, Task> handler in handlers.GetInvocationList())
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            await handler(savedSet, session);
        }
    }
}
