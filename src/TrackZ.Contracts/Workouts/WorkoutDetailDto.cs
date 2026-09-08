using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Contracts.Workouts;

public sealed record WorkoutDetailDto(
    Guid Id,
    WorkoutStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long Version,
    IReadOnlyList<WorkoutExerciseDto> Exercises);

public sealed record WorkoutExerciseDto(
    Guid Id,
    Guid ExerciseDefinitionId,
    string ExerciseName,
    TrackingMode TrackingMode,
    int Order,
    IReadOnlyList<WorkoutSetDto> Sets);

public sealed record WorkoutSetDto(
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

public sealed record ExerciseHistorySessionDto(
    Guid WorkoutId,
    DateTimeOffset CompletedAt,
    TrackingMode TrackingMode,
    decimal WeightedVolumeKg,
    IReadOnlyList<WorkoutSetDto> Sets);
