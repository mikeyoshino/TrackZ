using MediatR;
using TrackZ.Contracts.Sync;

namespace TrackZ.Application.Sync.Push;

public sealed record PushSyncCommand(IReadOnlyList<SyncOperationDto> Operations)
    : IRequest<SyncPushResponse>;

internal sealed record StartWorkoutSyncCommand(
    long BaseVersion,
    StartWorkoutSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record SaveSetSyncCommand(
    long BaseVersion,
    SaveSetSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record CompleteWorkoutSyncCommand(
    long BaseVersion,
    CompleteWorkoutSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record EditSetSyncCommand(
    long BaseVersion,
    EditSetSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record DeleteSetSyncCommand(
    long BaseVersion,
    DeleteSetSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record DeleteWorkoutSyncCommand(
    long BaseVersion,
    DeleteWorkoutSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record AddExerciseSyncCommand(
    long BaseVersion,
    AddExerciseSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record RemoveExerciseSyncCommand(
    long BaseVersion,
    RemoveExerciseSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record ReorderExercisesSyncCommand(
    long BaseVersion,
    ReorderExercisesSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record DeleteWorkoutExerciseSyncCommand(
    long BaseVersion,
    DeleteWorkoutExerciseSyncPayload Payload) : IRequest<SyncMutationResult>;

internal sealed record SyncMutationResult(
    SyncOperationStatus Status,
    long? ServerVersion,
    TrackZ.Contracts.Errors.BusinessErrorCode? ErrorCode,
    TrackZ.Domain.Workouts.WorkoutSession? Workout)
{
    public static SyncMutationResult Applied(TrackZ.Domain.Workouts.WorkoutSession workout) =>
        new(SyncOperationStatus.Applied, workout.Version, null, workout);

    public static SyncMutationResult Rejected(
        TrackZ.Contracts.Errors.BusinessErrorCode errorCode =
            TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest) =>
        new(SyncOperationStatus.Rejected, null, errorCode, null);

    public static SyncMutationResult Conflict(long serverVersion) =>
        new(SyncOperationStatus.Conflict, serverVersion, TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict, null);
}

internal sealed record StartWorkoutExerciseSyncPayload(
    Guid WorkoutExerciseId,
    Guid ExerciseDefinitionId,
    int TrackingMode,
    int? Order);

internal sealed record StartWorkoutSyncPayload(
    Guid WorkoutId,
    DateTimeOffset StartedAt,
    IReadOnlyList<StartWorkoutExerciseSyncPayload> Exercises);

internal sealed record SaveSetSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    int? Order,
    string? WeightKg,
    string? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt);

internal sealed record CompleteWorkoutSyncPayload(
    Guid WorkoutId,
    DateTimeOffset CompletedAt);

internal sealed record EditSetSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    string? WeightKg,
    string? AssistedKg,
    int Reps,
    DateTimeOffset UpdatedAt);

internal sealed record DeleteSetSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    DateTimeOffset DeletedAt);

internal sealed record DeleteWorkoutSyncPayload(
    Guid WorkoutId,
    DateTimeOffset DeletedAt);

internal sealed record AddExerciseSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid ExerciseDefinitionId,
    int TrackingMode,
    int? Order,
    DateTimeOffset AddedAt);

internal sealed record RemoveExerciseSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    DateTimeOffset DeletedAt);

internal sealed record ReorderExercisesSyncPayload(
    Guid WorkoutId,
    IReadOnlyList<Guid> WorkoutExerciseIds,
    DateTimeOffset ReorderedAt);

internal sealed record DeleteWorkoutExerciseSyncPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    DateTimeOffset DeletedAt);
