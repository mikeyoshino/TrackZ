namespace TrackZ.Application.Common.Interfaces;

public sealed record ProcessedExerciseImage(byte[] Master, byte[] Thumbnail, string ContentType, string DetectedContentType);

public interface IImageProcessor
{
    Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken);
}
