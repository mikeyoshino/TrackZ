namespace TrackZ.Contracts.Exercises;
public sealed record RequestImageUploadRequest(Guid? ExerciseId, string? ContentType, long? Length);
