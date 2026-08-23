using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed record SetEffortPromptRequest(
    Guid ExerciseDefinitionId,
    TrackingMode TrackingMode,
    LocalSet SavedSet,
    ExerciseHistorySessionDto? PreviousSession,
    Guid EffortOperationId);

public sealed class SetEffortPromptRequestedEventArgs(SetEffortPromptRequest request) : EventArgs
{
    public SetEffortPromptRequest Request { get; } =
        request ?? throw new ArgumentNullException(nameof(request));
}
