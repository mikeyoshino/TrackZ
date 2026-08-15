using TrackZ.Domain.Exercises;

namespace TrackZ.Contracts.Exercises;

public sealed record CreateCustomExerciseRequest(
    string? Name,
    BodyPart? BodyPart,
    TrackingMode? TrackingMode,
    Guid? LibraryImageId,
    string? UploadedImageKey);

public sealed record UpdateCustomExerciseRequest(
    string? Name,
    BodyPart? BodyPart,
    TrackingMode? TrackingMode,
    Guid? LibraryImageId,
    string? UploadedImageKey);
