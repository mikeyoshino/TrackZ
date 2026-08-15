namespace TrackZ.Domain.Sync;

public sealed class ProcessedClientOperation
{
    private ProcessedClientOperation()
    {
    }

    public Guid UserId { get; private set; }

    public Guid OperationId { get; private set; }

    public string RequestFingerprint { get; private set; } = string.Empty;

    public string ResultJson { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedClientOperation Create(
        Guid userId,
        Guid operationId,
        string requestFingerprint,
        string resultJson,
        DateTimeOffset processedAt)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A user identifier is required.", nameof(userId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        if (requestFingerprint is null || requestFingerprint.Length != 64)
            throw new ArgumentException("A SHA-256 request fingerprint is required.", nameof(requestFingerprint));
        if (string.IsNullOrWhiteSpace(resultJson))
            throw new ArgumentException("A serialized operation result is required.", nameof(resultJson));
        if (processedAt == default) throw new ArgumentException("A processed timestamp is required.", nameof(processedAt));

        return new ProcessedClientOperation
        {
            UserId = userId,
            OperationId = operationId,
            RequestFingerprint = requestFingerprint,
            ResultJson = resultJson,
            ProcessedAt = processedAt.ToUniversalTime()
        };
    }
}
