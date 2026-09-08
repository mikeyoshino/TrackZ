using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Application.Workouts;

public interface IWorkoutReadStore
{
    Task<WorkoutReadSession?> GetOwnedWorkoutAsync(
        Guid ownerId,
        Guid workoutId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkoutReadSession>> ListOwnedCompletedWorkoutsAsync(
        Guid ownerId,
        WorkoutCursor? after,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkoutReadSession>> ListOwnedExerciseHistoryAsync(
        Guid ownerId,
        Guid exerciseDefinitionId,
        WorkoutCursor? after,
        int take,
        CancellationToken cancellationToken);
}

public sealed record WorkoutReadSession(
    Guid Id,
    Guid OwnerId,
    WorkoutStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long Version,
    IReadOnlyList<WorkoutExerciseReadRow> Exercises);

public sealed record WorkoutExerciseReadRow(
    Guid Id,
    Guid ExerciseDefinitionId,
    string ExerciseName,
    TrackingMode TrackingMode,
    int Order,
    IReadOnlyList<WorkoutSetReadRow> Sets);

public sealed record WorkoutSetReadRow(
    Guid Id,
    int Order,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    SetEffortRating? Effort = null,
    int? PlateCount = null,
    int? EffortScore = null,
    bool? IsWarmup = null,
    bool? HasPain = null);
