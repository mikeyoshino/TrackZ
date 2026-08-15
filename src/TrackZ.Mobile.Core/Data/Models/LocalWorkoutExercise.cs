using TrackZ.Domain.Exercises;

namespace TrackZ.Mobile.Data.Models;

public sealed record LocalWorkoutExercise(
    Guid Id,
    Guid WorkoutId,
    Guid ExerciseDefinitionId,
    TrackingMode TrackingMode,
    int Order,
    DateTimeOffset? DeletedAt,
    long Version,
    long BaseVersion,
    IReadOnlyList<LocalSet> Sets);

public sealed record WorkoutExerciseSelection(Guid ExerciseDefinitionId, TrackingMode TrackingMode);
