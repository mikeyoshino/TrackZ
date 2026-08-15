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
        if (request.OperationId is { } operationId)
        {
            if (operationId == Guid.Empty) throw InvalidRequest("A client operation identifier is required.");
            var replay = await store.FindCustomByOperationAsync(currentUser.UserId, operationId, cancellationToken);
            if (replay is not null) return replay.Id;
        }

        if (request.UploadedImageKey is not null)
        {
            throw InvalidRequest("Uploaded image keys cannot be assigned directly.");
        }

        ExerciseImage? libraryImage = null;
        if (request.LibraryImageId is { } libraryImageId)
        {
            libraryImage = await store.FindPublishedLibraryImageAsync(libraryImageId, cancellationToken)
                ?? throw InvalidRequest("The selected library image is unavailable.");
        }

        ExerciseDefinition exercise;
        try
        {
            exercise = ExerciseDefinition.CreateCustom(
                currentUser.UserId,
                request.Name,
                request.BodyPart,
                request.TrackingMode,
                clientOperationId: request.OperationId);
        }
        catch (ArgumentException exception)
        {
            throw InvalidRequest(exception.Message);
        }
        exercise.SelectLibraryImage(libraryImage?.Id);

        if (!await store.TryCreateCustomAsync(exercise, cancellationToken))
        {
            if (request.OperationId is { } replayOperationId)
            {
                var replay = await store.FindCustomByOperationAsync(currentUser.UserId, replayOperationId, cancellationToken);
                if (replay is not null) return replay.Id;
            }
            throw new BusinessException(
                BusinessErrorCode.ExerciseNameDuplicate,
                "An active custom exercise with this name already exists.",
                409);
        }

        return exercise.Id;
    }

    private static BusinessException InvalidRequest(string message) => new(BusinessErrorCode.InvalidRequest, message, 400);
}
