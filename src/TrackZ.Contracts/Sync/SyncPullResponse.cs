using TrackZ.Domain.Workouts;

namespace TrackZ.Contracts.Sync;

public sealed record SyncPullResponse(
    IReadOnlyList<SyncChangeDto> Changes,
    string? NextCursor,
    bool HasMore);

public sealed record SyncChangeDto(
    long Sequence,
    string EntityType,
    Guid EntityId,
    long ServerVersion,
    bool IsDeleted,
    DateTimeOffset ChangedAt,
    SyncWorkoutDto Workout);

public sealed record SyncWorkoutDto(
    Guid Id,
    int Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    IReadOnlyList<SyncWorkoutExerciseDto> Exercises);

public sealed record SyncWorkoutExerciseDto(
    Guid Id,
    Guid ExerciseDefinitionId,
    int TrackingMode,
    int Order,
    DateTimeOffset? DeletedAt,
    long Version,
    IReadOnlyList<SyncSetDto> Sets);

public sealed record SyncSetDto(
    Guid Id,
    int Order,
    string? WeightKg,
    string? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    SetEffortRating? Effort = null,
    int? PlateCount = null,
    int? EffortScore = null,
    bool? IsWarmup = null,
    bool? HasPain = null);
