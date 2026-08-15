using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed record ImportedExerciseImage(string OriginalPath, string PreviewPath, string ContentType);

public sealed class LocalExerciseImageImporter
{
    private const int PreviewSize = 512;

    public async Task<ImportedExerciseImage> ImportAsync(
        string sourcePath,
        string contentType,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("Only JPEG, PNG, and WebP images are supported.", nameof(contentType))
        };
        if (new FileInfo(sourcePath).Length is <= 0 or > 5_000_000)
            throw new InvalidDataException("Exercise images must be between 1 byte and 5 MB.");
        var id = Guid.NewGuid().ToString("N");
        var originalDirectory = Path.Combine(destinationDirectory, "original");
        var previewDirectory = Path.Combine(destinationDirectory, "preview");
        Directory.CreateDirectory(originalDirectory);
        Directory.CreateDirectory(previewDirectory);
        var originalPath = Path.Combine(originalDirectory, id + extension);
        var previewPath = Path.Combine(previewDirectory, id + ".jpg");
        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = File.Create(originalPath))
                await source.CopyToAsync(destination, cancellationToken);

            using var image = await Image.LoadAsync(originalPath, cancellationToken);
            if (!string.Equals(image.Metadata.DecodedImageFormat?.DefaultMimeType, contentType, StringComparison.Ordinal))
                throw new InvalidDataException("The image content does not match its declared type.");
            image.Mutate(context => context
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(PreviewSize, PreviewSize)
                }));
            await image.SaveAsJpegAsync(previewPath, new JpegEncoder { Quality = 82 }, cancellationToken);
            return new ImportedExerciseImage(originalPath, previewPath, contentType);
        }
        catch (UnknownImageFormatException exception)
        {
            if (File.Exists(originalPath)) File.Delete(originalPath);
            if (File.Exists(previewPath)) File.Delete(previewPath);
            throw new InvalidDataException("The selected file is not a supported image.", exception);
        }
        catch
        {
            if (File.Exists(originalPath)) File.Delete(originalPath);
            if (File.Exists(previewPath)) File.Delete(previewPath);
            throw;
        }
    }
}
