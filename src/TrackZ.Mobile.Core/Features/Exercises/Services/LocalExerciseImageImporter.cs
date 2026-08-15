using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed record ImportedExerciseImage(string OriginalPath, string PreviewPath, string ContentType);

public sealed class LocalExerciseImageImporter
{
    private const int PreviewSize = 512;
    private const int MaximumBytes = 5_000_000;
    private const int MaximumDimension = 8192;
    private const int MaximumPixels = 20_000_000;

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

            await using var stagedOriginal = File.OpenRead(stagedOriginalPath);
            var format = Image.DetectFormat(stagedOriginal)
                ?? throw new InvalidDataException("The selected file is not a supported image.");
            if (format.DefaultMimeType is not ("image/jpeg" or "image/png" or "image/webp"))
                throw new InvalidDataException("The selected file is not a supported image.");
            if (!string.Equals(format.DefaultMimeType, contentType, StringComparison.Ordinal))
                throw new InvalidDataException("The image content does not match its declared type.");

            InspectContainerBeforeDecode(stagedOriginal, contentType);
            stagedOriginal.Position = 0;
            var imageInfo = Image.Identify(new DecoderOptions { MaxFrames = 2 }, stagedOriginal)
                ?? throw new InvalidDataException("The selected file is malformed.");
            ValidateDimensions(imageInfo.Width, imageInfo.Height);
            // Static formats may omit root-frame metadata; more than one entry is always animated.
            if (imageInfo.FrameMetadataCollection.Count > 1)
                throw new InvalidDataException("Animated images are not accepted.");

            stagedOriginal.Position = 0;
            using var image = await Image.LoadAsync(
                new DecoderOptions { MaxFrames = 2, SkipMetadata = false },
                stagedOriginal,
                cancellationToken);
            if (image.Frames.Count != 1)
                throw new InvalidDataException("Animated images are not accepted.");
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
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            DeleteIfExists(originalPath);
            DeleteIfExists(previewPath);
            throw new InvalidDataException("The selected file is malformed or unsupported.", exception);
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

    private static void InspectContainerBeforeDecode(Stream source, string contentType)
    {
        if (!source.CanSeek)
            throw new InvalidDataException("A bounded seekable image is required.");

        switch (contentType)
        {
            case "image/png":
                InspectPng(source);
                return;
            case "image/jpeg":
                InspectJpeg(source);
                return;
            case "image/webp":
                InspectWebp(source);
                return;
            default:
                throw new InvalidDataException("The selected file is not a supported image.");
        }
    }

    private static void InspectPng(Stream source)
    {
        source.Position = 0;
        Span<byte> signature = stackalloc byte[8];
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> imageHeader = stackalloc byte[13];
        if (source.Read(signature) != signature.Length ||
            !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("The selected file is malformed.");

        var sawHeader = false;
        var sawImageData = false;
        while (source.Position + 12 <= source.Length)
        {
            if (source.Read(chunkHeader) != chunkHeader.Length)
                throw new InvalidDataException("The selected file is malformed.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            if (length > source.Length - source.Position - 4)
                throw new InvalidDataException("The selected file is malformed.");
            var kind = chunkHeader[4..8];
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || length != 13)
                    throw new InvalidDataException("The selected file is malformed.");
                if (source.Read(imageHeader) != imageHeader.Length)
                    throw new InvalidDataException("The selected file is malformed.");
                ValidateDimensions(
                    BinaryPrimitives.ReadUInt32BigEndian(imageHeader[..4]),
                    BinaryPrimitives.ReadUInt32BigEndian(imageHeader[4..8]));
                sawHeader = true;
                source.Position += 4; // CRC
                continue;
            }
            if (!sawHeader)
                throw new InvalidDataException("The selected file is malformed.");
            if (kind.SequenceEqual("acTL"u8) || kind.SequenceEqual("fcTL"u8) || kind.SequenceEqual("fdAT"u8))
                throw new InvalidDataException("Animated images are not accepted.");
            if (kind.SequenceEqual("IDAT"u8)) sawImageData = true;
            var isEnd = kind.SequenceEqual("IEND"u8);
            source.Position += length + 4; // data + CRC
            if (isEnd)
            {
                if (length != 0 || !sawImageData || source.Position != source.Length)
                    throw new InvalidDataException("The selected file is malformed.");
                return;
            }
        }
        throw new InvalidDataException("The selected file is malformed.");
    }

    private static void InspectJpeg(Stream source)
    {
        source.Position = 0;
        if (source.ReadByte() != 0xFF || source.ReadByte() != 0xD8)
            throw new InvalidDataException("The selected file is malformed.");
        var foundDimensions = false;
        var inEntropy = false;
        Span<byte> size = stackalloc byte[2];
        Span<byte> dimensions = stackalloc byte[5];
        while (source.Position < source.Length)
        {
            var value = source.ReadByte();
            if (value < 0 || (value != 0xFF && !inEntropy))
                throw new InvalidDataException("The selected file is malformed.");
            if (value != 0xFF) continue;
            do { value = source.ReadByte(); } while (value == 0xFF);
            if (value < 0)
                throw new InvalidDataException("The selected file is malformed.");
            if (inEntropy && value == 0x00) continue;
            if (value == 0xD9)
            {
                if (!foundDimensions || source.Position != source.Length)
                    throw new InvalidDataException("The selected file is malformed.");
                return;
            }
            if (value is >= 0xD0 and <= 0xD7 or 0x01) continue;
            if (source.Read(size) != size.Length)
                throw new InvalidDataException("The selected file is malformed.");
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(size);
            if (segmentLength < 2 || segmentLength - 2 > source.Length - source.Position)
                throw new InvalidDataException("The selected file is malformed.");
            var isStartOfFrame = value is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF;
            if (isStartOfFrame)
            {
                if (segmentLength < 8 || source.Read(dimensions) != dimensions.Length)
                    throw new InvalidDataException("The selected file is malformed.");
                ValidateDimensions(
                    BinaryPrimitives.ReadUInt16BigEndian(dimensions[3..5]),
                    BinaryPrimitives.ReadUInt16BigEndian(dimensions[1..3]));
                foundDimensions = true;
                source.Position += segmentLength - 7;
            }
            else
            {
                if (value == 0xDA) inEntropy = true;
                source.Position += segmentLength - 2;
            }
        }
        throw new InvalidDataException("The selected file is malformed.");
    }

    private static void InspectWebp(Stream source)
    {
        source.Position = 0;
        Span<byte> riff = stackalloc byte[12];
        if (source.Read(riff) != riff.Length ||
            !riff[..4].SequenceEqual("RIFF"u8) ||
            !riff[8..12].SequenceEqual("WEBP"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(riff[4..8]) != source.Length - 8)
            throw new InvalidDataException("The selected file is malformed.");

        var imagePayloads = 0;
        var foundDimensions = false;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> prefix = stackalloc byte[10];
        while (source.Position < source.Length)
        {
            if (source.Length - source.Position < chunkHeader.Length || source.Read(chunkHeader) != chunkHeader.Length)
                throw new InvalidDataException("The selected file is malformed.");
            var kind = chunkHeader[..4];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
            var paddedLength = (long)length + (length & 1);
            if (paddedLength > source.Length - source.Position)
                throw new InvalidDataException("The selected file is malformed.");
            if (kind.SequenceEqual("ANIM"u8) || kind.SequenceEqual("ANMF"u8))
                throw new InvalidDataException("Animated images are not accepted.");
            if (!(kind.SequenceEqual("VP8 "u8) || kind.SequenceEqual("VP8L"u8) ||
                  kind.SequenceEqual("VP8X"u8) || kind.SequenceEqual("ALPH"u8) ||
                  kind.SequenceEqual("ICCP"u8) || kind.SequenceEqual("EXIF"u8) ||
                  kind.SequenceEqual("XMP "u8)))
                throw new InvalidDataException("The selected file is malformed.");

            var dataStart = source.Position;
            if (kind.SequenceEqual("VP8X"u8))
            {
                if (length != 10 || source.Read(prefix) != 10)
                    throw new InvalidDataException("The selected file is malformed.");
                if ((prefix[0] & 0x02) != 0)
                    throw new InvalidDataException("Animated images are not accepted.");
                ValidateDimensions(ReadUInt24(prefix[4..7]) + 1, ReadUInt24(prefix[7..10]) + 1);
                foundDimensions = true;
            }
            else if (kind.SequenceEqual("VP8 "u8))
            {
                if (++imagePayloads != 1 || length < 10 || source.Read(prefix) != 10 ||
                    !prefix[3..6].SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
                    throw new InvalidDataException("The selected file is malformed.");
                ValidateDimensions(
                    BinaryPrimitives.ReadUInt16LittleEndian(prefix[6..8]) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..10]) & 0x3FFF);
                foundDimensions = true;
            }
            else if (kind.SequenceEqual("VP8L"u8))
            {
                if (++imagePayloads != 1 || length < 5 || source.Read(prefix[..5]) != 5 || prefix[0] != 0x2F)
                    throw new InvalidDataException("The selected file is malformed.");
                ValidateDimensions(
                    1 + prefix[1] + ((prefix[2] & 0x3F) << 8),
                    1 + (prefix[2] >> 6) + (prefix[3] << 2) + ((prefix[4] & 0x0F) << 10));
                foundDimensions = true;
            }
            source.Position = dataStart + paddedLength;
        }
        if (!foundDimensions || imagePayloads != 1 || source.Position != source.Length)
            throw new InvalidDataException("The selected file is malformed.");
    }

    private static int ReadUInt24(ReadOnlySpan<byte> value) =>
        value[0] | (value[1] << 8) | (value[2] << 16);

    private static void ValidateDimensions(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension || width * height > MaximumPixels)
            throw new InvalidDataException("Image dimensions exceed the safe preview limit.");
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
