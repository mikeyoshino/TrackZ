using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Presentation;

public sealed class MauiTrackZMotion(IReduceMotionPreference reduceMotion) : ITrackZMotion
{
    public async Task PlaySetSavedAsync(
        VisualElement target,
        SetSavedOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        target.CancelAnimations();
        target.Opacity = 0;
        target.Scale = 1;
        if (reduceMotion.IsEnabled)
        {
            target.Opacity = 1;
            if (target.Handler is null)
                target.Opacity = 0;
            else
                await target.FadeToAsync(0, 180, Easing.Linear);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        target.Opacity = 1;
        if (outcome == SetSavedOutcome.PersonalRecord)
        {
            target.Scale = 0.94;
            await Task.WhenAll(
                target.ScaleToAsync(1.04, 180, Easing.CubicOut),
                target.FadeToAsync(1, 140, Easing.Linear));
            await target.ScaleToAsync(1, 180, Easing.CubicInOut);
        }
        else
        {
            await target.FadeToAsync(1, 160, Easing.Linear);
        }
        await target.FadeToAsync(0, 360, Easing.CubicIn);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task PlayWorkoutSummaryAsync(
        VisualElement target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        target.CancelAnimations();
        target.Opacity = 0;
        target.TranslationY = reduceMotion.IsEnabled ? 0 : 12;
        await Task.WhenAll(
            target.FadeToAsync(1, 220, Easing.CubicOut),
            target.TranslateToAsync(0, 0, 240, Easing.CubicOut));
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Cancel(VisualElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        MainThread.BeginInvokeOnMainThread(target.CancelAnimations);
    }
}
