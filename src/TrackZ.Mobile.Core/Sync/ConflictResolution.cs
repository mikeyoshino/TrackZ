namespace TrackZ.Mobile.Sync;

public interface IConflictResolution
{
    Task KeepServerAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<OutboxOperation> ApplyLocalAgainstVersionAsync(
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken = default);
}

public sealed class ConflictResolution(SyncCoordinator coordinator) : IConflictResolution
{
    public Task KeepServerAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        coordinator.KeepServerAsync(operationId, cancellationToken);

    public Task<OutboxOperation> ApplyLocalAgainstVersionAsync(
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken = default) =>
        coordinator.RebaseAsync(operationId, serverVersion, cancellationToken);
}
