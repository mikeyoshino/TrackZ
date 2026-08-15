using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.Custom;

public interface ICustomExerciseStore
{
    Task<bool> TryCreateCustomAsync(ExerciseDefinition exercise, CancellationToken cancellationToken);

    Task<ExerciseDefinition?> FindActiveCustomOwnedAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken);

    Task<bool> TrySaveCustomAsync(CancellationToken cancellationToken);
}
