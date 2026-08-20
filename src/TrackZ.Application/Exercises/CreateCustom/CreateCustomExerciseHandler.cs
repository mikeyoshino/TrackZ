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
        if (request.ExerciseId is not { } exerciseId || exerciseId == Guid.Empty)
            throw InvalidRequest("A client exercise identifier is required.");

        if (request.OperationId is { } operationId)
        {
            if (operationId == Guid.Empty) throw InvalidRequest("A client operation identifier is required.");
            var replay = await store.FindCustomByOperationAsync(currentUser.UserId, operationId, cancellationToken);
            if (replay is not null)
            {
                if (replay.Id != exerciseId)
                    throw InvalidRequest("The client operation is bound to another exercise identifier.");
                return replay.Id;
            }
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

        if (await store.FindAnyExerciseByIdAsync(exerciseId, cancellationToken) is not null)
            throw InvalidRequest("The client exercise identifier is already in use.");

        ExerciseDefinition exercise;
        try
        {
            exercise = ExerciseDefinition.CreateCustom(
                currentUser.UserId,
                exerciseId,
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
                if (replay is not null)
                {
                    if (replay.Id != exerciseId)
                        throw InvalidRequest(
                            "The client operation is bound to another exercise identifier.");
                    return replay.Id;
                }
            }
            if (await store.FindAnyExerciseByIdAsync(exerciseId, cancellationToken) is not null)
                throw InvalidRequest("The client exercise identifier is already in use.");
            throw new BusinessException(
                BusinessErrorCode.ExerciseNameDuplicate,
                "An active custom exercise with this name already exists.",
                409);
        }

        return exercise.Id;
    }

    private static BusinessException InvalidRequest(string message) => new(BusinessErrorCode.InvalidRequest, message, 400);
}
