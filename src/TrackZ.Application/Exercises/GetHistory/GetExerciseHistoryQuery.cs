using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Workouts;
using TrackZ.Contracts.Common;
using TrackZ.Contracts.Workouts;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.GetHistory;

public sealed record GetExerciseHistoryQuery(Guid ExerciseId, string? Cursor, int PageSize)
    : IRequest<CursorPage<ExerciseHistorySessionDto>>;

public sealed class GetExerciseHistoryHandler(
    IWorkoutReadStore store,
    ICurrentUser currentUser,
    IWorkoutCursorCodec cursorCodec)
    : IRequestHandler<GetExerciseHistoryQuery, CursorPage<ExerciseHistorySessionDto>>
{
    public async Task<CursorPage<ExerciseHistorySessionDto>> Handle(
        GetExerciseHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var scope = new WorkoutCursorScope(
            currentUser.UserId,
            WorkoutCursorPurpose.ExerciseHistory,
            request.ExerciseId);
        var after = string.IsNullOrWhiteSpace(request.Cursor) ? null : cursorCodec.Decode(request.Cursor, scope);
        var rows = request.ExerciseId == Guid.Empty
            ? []
            : await store.ListOwnedExerciseHistoryAsync(
                currentUser.UserId, request.ExerciseId, after, pageSize + 1, cancellationToken);
        var pageRows = rows.Take(pageSize).ToList();
        var items = pageRows.Select(session =>
        {
            var exercise = session.Exercises.Single(item => item.ExerciseDefinitionId == request.ExerciseId);
            var orderedSets = exercise.Sets.OrderBy(set => set.Order).ToList();
            var volume = exercise.TrackingMode == TrackingMode.Weighted
                ? orderedSets.Sum(set => set.WeightKg!.Value * set.Reps)
                : 0m;
            return new ExerciseHistorySessionDto(
                session.Id,
                session.CompletedAt!.Value,
                exercise.TrackingMode,
                volume,
                orderedSets.Select(WorkoutDtoMapper.ToDto).ToList());
        }).ToList();
        var nextCursor = rows.Count > pageSize
            ? cursorCodec.Encode(scope, pageRows[^1].CompletedAt!.Value, pageRows[^1].Id)
            : null;
        return new CursorPage<ExerciseHistorySessionDto>(items, nextCursor);
    }
}
