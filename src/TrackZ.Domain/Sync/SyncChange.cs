namespace TrackZ.Domain.Sync;

public sealed class SyncChange
{
    private SyncChange()
    {
    }

    public long Sequence { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid OperationId { get; private set; }
    public Guid EntityId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public long ServerVersion { get; private set; }
    public bool IsDeleted { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; private set; }

    public static SyncChange Create(
        Guid ownerId,
        Guid operationId,
        Guid entityId,
        long serverVersion,
        bool isDeleted,
        string payloadJson,
        DateTimeOffset changedAt)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerId));
        if (operationId == Guid.Empty) throw new ArgumentException("An operation is required.", nameof(operationId));
        if (entityId == Guid.Empty) throw new ArgumentException("An entity is required.", nameof(entityId));
        if (serverVersion < 0) throw new ArgumentOutOfRangeException(nameof(serverVersion));
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new ArgumentException("A payload is required.", nameof(payloadJson));
        if (changedAt == default) throw new ArgumentException("A changed timestamp is required.", nameof(changedAt));
        return new SyncChange
        {
            OwnerId = ownerId,
            OperationId = operationId,
            EntityId = entityId,
            EntityType = "Workout",
            ServerVersion = serverVersion,
            IsDeleted = isDeleted,
            PayloadJson = payloadJson,
            ChangedAt = changedAt.ToUniversalTime()
        };
    }
}
