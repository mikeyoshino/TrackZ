using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;

namespace TrackZ.Application.Exercises.UpdateCustom;

public sealed class UpdateCustomExerciseHandler(ICustomExerciseStore store, ICurrentUser currentUser)
    : IRequestHandler<UpdateCustomExerciseCommand>
{
    public async Task Handle(UpdateCustomExerciseCommand request, CancellationToken cancellationToken)
    {
        var exercise = await store.FindActiveCustomOwnedAsync(request.ExerciseId, currentUser.UserId, cancellationToken)
            ?? throw NotFound();

        if (request.LibraryImageId is not null || request.UploadedImageKey is not null)
        {
            throw InvalidRequest("Image selection is not available yet.");
        }

        try
        {
            if (request.TrackingMode is { } trackingMode)
            {
                exercise.ChangeTrackingMode(trackingMode);
            }

            exercise.UpdateDetails(request.Name, request.BodyPart);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            throw InvalidRequest(exception.Message);
        }

        if (!await store.TrySaveCustomAsync(cancellationToken))
        {
            throw new BusinessException(BusinessErrorCode.ExerciseNameDuplicate, "An active custom exercise with this name already exists.", 409);
        }
    }

    private static BusinessException NotFound() => new(BusinessErrorCode.ExerciseNotFound, "The exercise was not found.", 404);

    private static BusinessException InvalidRequest(string message) => new(BusinessErrorCode.InvalidRequest, message, 400);
}
