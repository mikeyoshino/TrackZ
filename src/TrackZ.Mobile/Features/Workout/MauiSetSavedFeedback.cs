using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed class MauiSetSavedFeedback(
    Func<bool>? hapticsEnabled = null,
    Func<Action, Task>? invokeOnMainThread = null,
    Action? performHaptic = null,
    Action<SetSavedOutcome>? performOutcomeHaptic = null,
    WorkoutTextSet? text = null) : ISetSavedFeedback
{
    private readonly WorkoutTextSet _text = text ?? WorkoutResources.Current;
    private readonly Func<bool> _hapticsEnabled = hapticsEnabled
        ?? (() => Preferences.Default.Get("trackz_haptics_enabled", true));
    private readonly Func<Action, Task> _invokeOnMainThread = invokeOnMainThread
        ?? MainThread.InvokeOnMainThreadAsync;
    private readonly Action<SetSavedOutcome> _performHaptic = performOutcomeHaptic
        ?? (performHaptic is not null
            ? _ => performHaptic()
            : outcome => HapticFeedback.Default.Perform(
                outcome == SetSavedOutcome.PersonalRecord
                    ? HapticFeedbackType.LongPress
                    : HapticFeedbackType.Click));

    public event Func<SetSavedPresentation, SetSavedFeedbackSession, Task>? Saved;

    public async Task SetSavedAsync(SetSavedPresentation presentation, SetSavedFeedbackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.CancellationToken.ThrowIfCancellationRequested();
        if (_hapticsEnabled())
        {
            var started = false;
            await _invokeOnMainThread(() =>
                started = session.TryStartPhase(() => _performHaptic(presentation.Outcome)));
            if (!started) return;
            session.CancellationToken.ThrowIfCancellationRequested();
        }

        var handlers = Saved;
        if (handlers is null) return;
        foreach (Func<SetSavedPresentation, SetSavedFeedbackSession, Task> handler in handlers.GetInvocationList())
        {
            session.CancellationToken.ThrowIfCancellationRequested();
            await handler(presentation, session);
        }
    }

    public Task SetSavedAsync(LocalSet savedSet, SetSavedFeedbackSession session) =>
        SetSavedAsync(new SetSavedPresentation(
            savedSet,
            SetSavedOutcome.Saved,
            _text.SetSaved,
            _text.SetSavedSecondary), session);
}
