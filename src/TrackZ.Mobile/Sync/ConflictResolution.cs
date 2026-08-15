namespace TrackZ.Mobile.Sync;

public sealed class ConflictResolution(SyncCoordinator coordinator)
{
    public Task KeepServerAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        coordinator.KeepServerAsync(operationId, cancellationToken);

    public Task<OutboxOperation> ApplyLocalAgainstVersionAsync(
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken = default) =>
        coordinator.RebaseAsync(operationId, serverVersion, cancellationToken);
}
