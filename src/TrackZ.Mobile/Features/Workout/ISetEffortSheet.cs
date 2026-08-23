namespace TrackZ.Mobile.Features.Workout;

public interface ISetEffortSheet
{
    Task PresentAsync(
        SetEffortPromptRequest request,
        Func<HypertrophyGuidanceResult, bool> applyToDraft,
        CancellationToken cancellationToken = default);
}
