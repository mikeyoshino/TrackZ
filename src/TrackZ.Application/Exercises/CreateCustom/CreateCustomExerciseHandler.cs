using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.Custom;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.CreateCustom;

public sealed class CreateCustomExerciseHandler(ICustomExerciseStore store, ICurrentUser currentUser)
    : IRequestHandler<CreateCustomExerciseCommand, Guid>
{
    public async Task<Guid> Handle(CreateCustomExerciseCommand request, CancellationToken cancellationToken)
    {
        if (request.LibraryImageId is not null || request.UploadedImageKey is not null)
        {
            // Task 4 owns creating independently verifiable upload records. The current aggregate has
            // no safe association for an existing asset, so untrusted identifiers are deliberately rejected.
            throw InvalidRequest("Image selection is not available yet.");
        }

        ExerciseDefinition exercise;
        try
        {
            exercise = ExerciseDefinition.CreateCustom(
                currentUser.UserId,
                request.Name,
                request.BodyPart,
                request.TrackingMode);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }

        if (!await store.TryCreateCustomAsync(exercise, cancellationToken))
        {
            throw new BusinessException(
                BusinessErrorCode.ExerciseNameDuplicate,
                "An active custom exercise with this name already exists.",
                409);
        }

        return exercise.Id;
    }

    private static BusinessException InvalidRequest(string message) => new(BusinessErrorCode.InvalidRequest, message, 400);
}
