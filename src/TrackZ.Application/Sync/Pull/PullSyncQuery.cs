using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Sync;

namespace TrackZ.Application.Sync.Pull;

public sealed record PullSyncQuery(string? Cursor, int PageSize = 100) : IRequest<SyncPullResponse>;

public interface ISyncPullStore
{
    Task<IReadOnlyList<SyncChange>> ReadChangesAsync(
        Guid ownerId, long afterSequence, int take, CancellationToken cancellationToken);
}

public interface ISyncCursorCodec
{
    long Decode(string cursor, Guid expectedOwnerId);
    string Encode(Guid ownerId, long sequence);
}

public sealed class PullSyncHandler(
    ISyncPullStore store,
    ISyncCursorCodec cursorCodec,
    ICurrentUser currentUser) : IRequestHandler<PullSyncQuery, SyncPullResponse>
{
    public async Task<SyncPullResponse> Handle(PullSyncQuery request, CancellationToken cancellationToken)
    {
        if (request.PageSize is < 1 or > 100)
            throw new BusinessException(BusinessErrorCode.InvalidRequest, "The page size is invalid.", 400);
        var after = request.Cursor is null ? 0 : cursorCodec.Decode(request.Cursor, currentUser.UserId);
        var rows = await store.ReadChangesAsync(
            currentUser.UserId, after, request.PageSize + 1, cancellationToken);
        var page = rows.Take(request.PageSize).ToArray();
        var changes = page.Select(change => System.Text.Json.JsonSerializer.Deserialize<SyncWorkoutDto>(
                change.PayloadJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("A stored sync payload is invalid."))
            .Select((workout, index) => new SyncChangeDto(
                page[index].Sequence,
                page[index].EntityType,
                page[index].EntityId,
                page[index].ServerVersion,
                page[index].IsDeleted,
                page[index].ChangedAt,
                workout))
            .ToArray();
        var nextCursor = page.Length == 0
            ? request.Cursor
            : cursorCodec.Encode(currentUser.UserId, page[^1].Sequence);
        return new SyncPullResponse(changes, nextCursor, rows.Count > request.PageSize);
    }
}
