using System.Text.Json;

namespace TrackZ.Contracts.Sync;

public sealed record SyncPushRequest(IReadOnlyList<SyncOperationDto> Operations);

public sealed record SyncOperationDto(
    Guid OperationId,
    string EntityType,
    string Action,
    JsonElement Payload,
    long? BaseVersion);
