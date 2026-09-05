using TrackZ.Contracts.Workouts;

namespace TrackZ.Application.Workouts;

internal static class WorkoutDtoMapper
{
    public static WorkoutDetailDto ToDetail(WorkoutReadSession session) => new(
        session.Id,
        session.Status,
        session.StartedAt,
        session.CompletedAt,
        session.Version,
        session.Exercises
            .OrderBy(exercise => exercise.Order)
            .Select(exercise => new WorkoutExerciseDto(
                exercise.Id,
                exercise.ExerciseDefinitionId,
                exercise.ExerciseName,
                exercise.TrackingMode,
                exercise.Order,
                exercise.Sets.OrderBy(set => set.Order).Select(ToDto).ToList()))
            .ToList());

    public static WorkoutSetDto ToDto(WorkoutSetReadRow set) => new(
        set.Id,
        set.Order,
        set.WeightKg,
        set.AssistedKg,
        set.Reps,
        set.CompletedAt,
        set.UpdatedAt,
        set.Effort,
        set.PlateCount);
}
