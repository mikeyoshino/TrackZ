using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.Custom;

public interface ICustomExerciseStore
{
    Task<bool> TryCreateCustomAsync(ExerciseDefinition exercise, CancellationToken cancellationToken);

    Task<ExerciseDefinition?> FindCustomByOperationAsync(Guid ownerId, Guid operationId, CancellationToken cancellationToken);

    Task<ExerciseDefinition?> FindAnyExerciseByIdAsync(Guid exerciseId, CancellationToken cancellationToken);

    Task<ExerciseImage?> FindPublishedLibraryImageAsync(Guid imageId, CancellationToken cancellationToken);

    Task<ExerciseDefinition?> FindActiveCustomOwnedAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken);

    Task<bool> TrySaveCustomAsync(CancellationToken cancellationToken);
}
