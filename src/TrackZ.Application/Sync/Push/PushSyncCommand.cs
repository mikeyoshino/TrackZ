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

internal sealed record SyncMutationResult(
    SyncOperationStatus Status,
    long? ServerVersion,
    TrackZ.Contracts.Errors.BusinessErrorCode? ErrorCode)
{
    public static SyncMutationResult Applied(long serverVersion) =>
        new(SyncOperationStatus.Applied, serverVersion, null);

    public static SyncMutationResult Rejected() =>
        new(SyncOperationStatus.Rejected, null, TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest);

    public static SyncMutationResult Conflict(long serverVersion) =>
        new(SyncOperationStatus.Conflict, serverVersion, TrackZ.Contracts.Errors.BusinessErrorCode.VersionConflict);
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
