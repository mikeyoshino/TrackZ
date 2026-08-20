using TrackZ.Domain.Exercises;

namespace TrackZ.Contracts.Exercises;

public sealed record CreateCustomExerciseRequest(
    string? Name,
    BodyPart? BodyPart,
    TrackingMode? TrackingMode,
    Guid? LibraryImageId,
    string? UploadedImageKey,
    Guid? OperationId = null,
    Guid? ExerciseId = null);

public sealed record UpdateCustomExerciseRequest(
    string? Name,
    BodyPart? BodyPart,
    TrackingMode? TrackingMode,
    Guid? LibraryImageId,
    string? UploadedImageKey);
