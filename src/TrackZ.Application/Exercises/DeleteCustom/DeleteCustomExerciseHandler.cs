using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;

namespace TrackZ.Application.Exercises.DeleteCustom;

public sealed class DeleteCustomExerciseHandler(ICustomExerciseStore store, ICurrentUser currentUser)
    : IRequestHandler<DeleteCustomExerciseCommand>
{
    public async Task Handle(DeleteCustomExerciseCommand request, CancellationToken cancellationToken)
    {
        var exercise = await store.FindActiveCustomOwnedAsync(request.ExerciseId, currentUser.UserId, cancellationToken)
            ?? throw new BusinessException(BusinessErrorCode.ExerciseNotFound, "The exercise was not found.", 404);

        exercise.Archive();
        await store.TrySaveCustomAsync(cancellationToken);
    }
}
