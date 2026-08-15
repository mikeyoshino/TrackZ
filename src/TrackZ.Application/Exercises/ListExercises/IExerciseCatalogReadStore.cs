using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.ListExercises;

public interface IExerciseCatalogReadStore
{
    Task<IReadOnlyList<CatalogExerciseReadItem>> ListAsync(
        Guid userId,
        BodyPart? bodyPart,
        string? normalizedSearch,
        CatalogCursor? after,
        int take,
        CancellationToken cancellationToken);
}

public sealed record CatalogExerciseReadItem(
    ExerciseSummaryDto Summary,
    string OrderingName,
    Guid OrderingId);
