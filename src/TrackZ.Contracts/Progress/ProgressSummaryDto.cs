using TrackZ.Domain.Exercises;

namespace TrackZ.Contracts.Progress;

public sealed record ProgressSummaryDto(
    decimal TotalVolumeKg,
    decimal WeeklyVolumeKg,
    int CompletedWorkouts,
    int ExercisesProgressing,
    IReadOnlyList<ExerciseProgressSummaryDto> PersonalRecords);

public sealed record ExerciseProgressSummaryDto(
    Guid ExerciseId,
    string ExerciseName,
    TrackingMode TrackingMode,
    DateTimeOffset LastPerformedAt,
    decimal? LastWeightKg,
    decimal? LastAssistedKg,
    int LastReps,
    decimal? BestWeightKg,
    decimal? BestAssistedKg,
    int BestReps,
    int? LastPlateCount = null,
    int? BestPlateCount = null);
