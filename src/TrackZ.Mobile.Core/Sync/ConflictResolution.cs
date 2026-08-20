namespace TrackZ.Mobile.Sync;

public interface IConflictResolution
{
    Task KeepServerAsync(Guid operationId, CancellationToken cancellationToken = default);

    Task<OutboxOperation> ApplyLocalAgainstVersionAsync(
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken = default);
}

public sealed class ConflictResolution(
    SyncCoordinator coordinator,
    IWorkoutSyncTrigger? syncTrigger = null) : IConflictResolution
{
    public async Task KeepServerAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await coordinator.KeepServerAsync(operationId, cancellationToken);
        syncTrigger?.NotifyMutation();
    }

    public async Task<OutboxOperation> ApplyLocalAgainstVersionAsync(
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken = default)
    {
        var replacement = await coordinator.RebaseAsync(operationId, serverVersion, cancellationToken);
        syncTrigger?.NotifyMutation();
        return replacement;
    }
}
