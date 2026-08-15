using MediatR;
using TrackZ.Contracts.Common;
using TrackZ.Contracts.Exercises;

namespace TrackZ.Application.Exercises.ListExercises;

public sealed class ListExercisesHandler(
    IExerciseCatalogReadStore catalog,
    ICurrentUser currentUser,
    IExerciseCursorCodec cursorCodec) : IRequestHandler<ListExercisesQuery, CursorPage<ExerciseSummaryDto>>
{
    public async Task<CursorPage<ExerciseSummaryDto>> Handle(ListExercisesQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var cursor = string.IsNullOrWhiteSpace(request.Cursor) ? null : cursorCodec.Decode(request.Cursor);
        var normalizedSearch = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToUpperInvariant();
        var rows = await catalog.ListAsync(currentUser.UserId, request.BodyPart, normalizedSearch, cursor, pageSize + 1, cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToList();
        var nextCursor = hasMore
            ? cursorCodec.Encode(new CatalogCursor(1, items[^1].OrderingName, items[^1].OrderingId))
            : null;

        return new CursorPage<ExerciseSummaryDto>(items.Select(item => item.Summary).ToList(), nextCursor);
    }
}
