using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data.Models;

namespace TrackZ.Mobile.Features.Workout;

public sealed class SetEffortRecordingUnavailableException()
    : InvalidOperationException(
        "The saved set is no longer available in an active workout.");

public interface ISetEffortRecorder
{
    Task<LocalSet> RecordSetEffortAsync(
        Guid exerciseDefinitionId,
        Guid setId,
        SetEffortRating effort,
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<LocalWorkout?> RestoreActiveAsync(
        CancellationToken cancellationToken = default);
}
