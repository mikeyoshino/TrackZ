using System.Text.Json.Serialization;
using TrackZ.Contracts.Errors;

namespace TrackZ.Contracts.Sync;

public sealed record SyncPushResponse(IReadOnlyList<SyncOperationResultDto> Results);

public sealed record SyncOperationResultDto(
    Guid OperationId,
    SyncOperationStatus Status,
    long? ServerVersion,
    BusinessErrorCode? ErrorCode);

[JsonConverter(typeof(JsonStringEnumConverter<SyncOperationStatus>))]
public enum SyncOperationStatus
{
    Applied = 1,
    Rejected = 2,
    Conflict = 3,
    Retryable = 4
}
