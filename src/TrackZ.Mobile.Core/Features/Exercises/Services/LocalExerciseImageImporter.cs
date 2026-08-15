using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed record ImportedExerciseImage(string OriginalPath, string PreviewPath, string ContentType);

public sealed class LocalExerciseImageImporter
{
    private const int PreviewSize = 512;
    private const int MaximumBytes = 5_000_000;

    public async Task<ImportedExerciseImage> ImportAsync(
        string sourcePath,
        string contentType,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (new FileInfo(sourcePath).Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Exercise images must be between 1 byte and 5 MB.");
        await using var source = File.OpenRead(sourcePath);
        return await ImportAsync(
            source,
            contentType,
            destinationDirectory,
            "standalone",
            cancellationToken);
    }

    public async Task<ImportedExerciseImage> ImportAsync(
        Stream source,
        string contentType,
        string destinationDirectory,
        string sessionScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionScope);
        if (!source.CanRead) throw new ArgumentException("The image stream must be readable.", nameof(source));
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("Only JPEG, PNG, and WebP images are supported.", nameof(contentType))
        };
        var id = Guid.NewGuid().ToString("N");
        var originalDirectory = Path.Combine(destinationDirectory, "original");
        var previewDirectory = Path.Combine(destinationDirectory, "preview");
        var originalPath = Path.Combine(originalDirectory, id + extension);
        var previewPath = Path.Combine(previewDirectory, id + ".jpg");
        var stagingDirectory = Path.Combine(destinationDirectory, ".staging", sessionScope, id);
        var stagedOriginalPath = Path.Combine(stagingDirectory, "original" + extension);
        var stagedPreviewPath = Path.Combine(stagingDirectory, "preview.jpg");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await using (var destination = File.Create(stagedOriginalPath))
                await CopyWithLimitAsync(source, destination, cancellationToken);

            using var image = await Image.LoadAsync(stagedOriginalPath, cancellationToken);
            if (!string.Equals(image.Metadata.DecodedImageFormat?.DefaultMimeType, contentType, StringComparison.Ordinal))
                throw new InvalidDataException("The image content does not match its declared type.");
            image.Mutate(context => context
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(PreviewSize, PreviewSize)
                }));
            await image.SaveAsJpegAsync(stagedPreviewPath, new JpegEncoder { Quality = 82 }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(originalDirectory);
            Directory.CreateDirectory(previewDirectory);
            File.Move(stagedOriginalPath, originalPath);
            File.Move(stagedPreviewPath, previewPath);
            return new ImportedExerciseImage(originalPath, previewPath, contentType);
        }
        catch (UnknownImageFormatException exception)
        {
            DeleteIfExists(originalPath);
            DeleteIfExists(previewPath);
            throw new InvalidDataException("The selected file is not a supported image.", exception);
        }
        catch
        {
            DeleteIfExists(originalPath);
            DeleteIfExists(previewPath);
            throw;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            copied += read;
            if (copied > MaximumBytes)
                throw new InvalidDataException("Exercise images must be between 1 byte and 5 MB.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (copied == 0)
            throw new InvalidDataException("Exercise images must be between 1 byte and 5 MB.");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
