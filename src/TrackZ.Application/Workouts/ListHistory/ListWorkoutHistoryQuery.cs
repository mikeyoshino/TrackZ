using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Common;
using TrackZ.Contracts.Workouts;

namespace TrackZ.Application.Workouts.ListHistory;

public sealed record ListWorkoutHistoryQuery(string? Cursor, int PageSize)
    : IRequest<CursorPage<WorkoutDetailDto>>;

public sealed class ListWorkoutHistoryHandler(
    IWorkoutReadStore store,
    ICurrentUser currentUser,
    IWorkoutCursorCodec cursorCodec)
    : IRequestHandler<ListWorkoutHistoryQuery, CursorPage<WorkoutDetailDto>>
{
    public async Task<CursorPage<WorkoutDetailDto>> Handle(
        ListWorkoutHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var scope = new WorkoutCursorScope(currentUser.UserId, WorkoutCursorPurpose.WorkoutHistory, null);
        var after = string.IsNullOrWhiteSpace(request.Cursor) ? null : cursorCodec.Decode(request.Cursor, scope);
        var rows = await store.ListOwnedCompletedWorkoutsAsync(
            currentUser.UserId, after, pageSize + 1, cancellationToken);
        var items = rows.Take(pageSize).ToList();
        var nextCursor = rows.Count > pageSize
            ? cursorCodec.Encode(scope, items[^1].CompletedAt!.Value, items[^1].Id)
            : null;
        return new CursorPage<WorkoutDetailDto>(items.Select(WorkoutDtoMapper.ToDetail).ToList(), nextCursor);
    }
}
