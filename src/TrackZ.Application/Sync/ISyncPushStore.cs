using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Sync;
using TrackZ.Domain.Workouts;

namespace TrackZ.Application.Sync;

public interface ISyncPushStore
{
    Task<IAppDbTransaction> BeginSyncTransactionAsync(CancellationToken cancellationToken);

    Task AcquireOperationLockAsync(Guid userId, Guid operationId, CancellationToken cancellationToken);

    Task AcquireWorkoutLockAsync(Guid workoutId, CancellationToken cancellationToken);

    Task<ProcessedClientOperation?> FindProcessedOperationAsync(
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken);

    Task<WorkoutSession?> FindOwnedWorkoutAsync(
        Guid userId,
        Guid workoutId,
        CancellationToken cancellationToken);

    Task<Guid?> FindWorkoutOwnerAsync(Guid workoutId, CancellationToken cancellationToken);

    Task<bool> AreExerciseDefinitionsAvailableAsync(
        Guid userId,
        IReadOnlyList<Guid> exerciseDefinitionIds,
        CancellationToken cancellationToken);

    Task<bool> AreWorkoutExerciseIdentifiersAvailableAsync(
        IReadOnlyList<Guid> workoutExerciseIds,
        CancellationToken cancellationToken);

    Task<bool> IsSetIdentifierAvailableAsync(Guid setId, CancellationToken cancellationToken);

    void AddWorkout(WorkoutSession workout);

    void AddProcessedOperation(ProcessedClientOperation operation);

    void AddSyncChange(SyncChange change);

    Task SaveSyncChangesAsync(CancellationToken cancellationToken);

    bool IsTransient(Exception exception);

    void ClearSyncTracking();
}
