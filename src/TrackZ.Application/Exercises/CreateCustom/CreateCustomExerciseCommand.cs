using MediatR;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.CreateCustom;

public sealed record CreateCustomExerciseCommand(
    string Name,
    BodyPart BodyPart,
    TrackingMode TrackingMode,
    Guid? LibraryImageId,
    string? UploadedImageKey,
    Guid? OperationId = null,
    Guid? ExerciseId = null) : IRequest<Guid>;
