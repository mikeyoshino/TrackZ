using MediatR;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Exercises.UpdateCustom;

public sealed record UpdateCustomExerciseCommand(
    Guid ExerciseId,
    string Name,
    BodyPart BodyPart,
    TrackingMode? TrackingMode,
    Guid? LibraryImageId,
    string? UploadedImageKey) : IRequest;
