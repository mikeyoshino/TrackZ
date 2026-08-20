using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Presentation;

public interface ITrackZMotion
{
    Task PlaySetSavedAsync(
        VisualElement target,
        SetSavedOutcome outcome,
        CancellationToken cancellationToken);

    Task PlayWorkoutSummaryAsync(
        VisualElement target,
        CancellationToken cancellationToken);

    void Cancel(VisualElement target);
}
