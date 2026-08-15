using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Workouts;

namespace TrackZ.Application.Workouts.GetWorkout;

public sealed record GetWorkoutQuery(Guid WorkoutId) : IRequest<WorkoutDetailDto>;

public sealed class GetWorkoutHandler(IWorkoutReadStore store, ICurrentUser currentUser)
    : IRequestHandler<GetWorkoutQuery, WorkoutDetailDto>
{
    public async Task<WorkoutDetailDto> Handle(GetWorkoutQuery request, CancellationToken cancellationToken)
    {
        var workout = request.WorkoutId == Guid.Empty
            ? null
            : await store.GetOwnedWorkoutAsync(currentUser.UserId, request.WorkoutId, cancellationToken);
        return workout is null
            ? throw new BusinessException(
                BusinessErrorCode.WorkoutNotFound,
                "The workout was not found.",
                404)
            : WorkoutDtoMapper.ToDetail(workout);
    }
}
