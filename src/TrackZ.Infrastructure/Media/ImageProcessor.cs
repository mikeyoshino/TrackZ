using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Media;
public sealed class ImageProcessor : IImageProcessor
{
    private const int MaxPixels = 20_000_000;
    private const int MaxDimension = 8192;
    public async Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!source.CanSeek) throw new InvalidDataException("A bounded seekable stream is required.");
        try
        {
            source.Position = 0;
            var format = Image.DetectFormat(source) ?? throw new InvalidDataException("Unknown image format.");
            var detected = format.DefaultMimeType;
            if (detected is not ("image/jpeg" or "image/png" or "image/webp")) throw new InvalidDataException("Unsupported image format.");
            EnsurePngDimensionsBeforeDecode(source, detected);
            if (!HasCompleteContainer(source, detected)) throw new InvalidDataException("Malformed image.");
            source.Position = 0;
            var info = Image.Identify(new DecoderOptions { MaxFrames = 2 }, source) ?? throw new InvalidDataException("Malformed image.");
            if (info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension || (long)info.Width * info.Height > MaxPixels) throw new InvalidDataException("Image dimensions exceed the processing limit.");
            source.Position = 0;
            using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 2, SkipMetadata = false }, source, cancellationToken);
            if (image.Frames.Count != 1) throw new InvalidDataException("Animated images are not accepted.");
            image.Mutate(context => context.AutoOrient());
            var master = await EncodeAsync(image, 1024, cancellationToken);
            var thumbnail = await EncodeAsync(image, 320, cancellationToken);
            return new ProcessedExerciseImage(master, thumbnail, "image/jpeg", detected);
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidDataException("Malformed image.", exception);
        }
    }
    private static async Task<byte[]> EncodeAsync(Image image, int maximum, CancellationToken cancellationToken)
    {
        using var copy = image.Width <= maximum && image.Height <= maximum
            ? image.Clone(context => { })
            : image.Clone(context => context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maximum, maximum), Sampler = KnownResamplers.Lanczos3 }));
        copy.Metadata.ExifProfile = null; copy.Metadata.IccProfile = null; copy.Metadata.IptcProfile = null; copy.Metadata.XmpProfile = null;
        await using var output = new MemoryStream();
        await copy.SaveAsync(output, new JpegEncoder { Quality = 85 }, cancellationToken);
        return output.ToArray();
    }
    private static bool HasCompleteContainer(Stream source, string contentType)
    {
        if (contentType == "image/jpeg")
        {
            return IsExactJpeg(source);
        }
        if (contentType == "image/png")
        {
            return IsExactPng(source);
        }
        if (source.Length < 8) return false;
        source.Position = 4;
        Span<byte> size = stackalloc byte[4];
        return source.Read(size) == size.Length && BinaryPrimitives.ReadUInt32LittleEndian(size) == source.Length - 8;
    }
    private static void EnsurePngDimensionsBeforeDecode(Stream source, string contentType)
    {
        if (contentType != "image/png") return;
        source.Position = 8;
        Span<byte> header = stackalloc byte[16];
        if (source.Read(header) != header.Length || !header[..8].SequenceEqual(new byte[] { 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 })) throw new InvalidDataException("Malformed image.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(header[8..12]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[12..16]);
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels) throw new InvalidDataException("Image dimensions exceed the processing limit.");
    }
    private static bool IsExactPng(Stream source)
    {
        if (source.Length < 20) return false;
        source.Position = 0;
        Span<byte> signature = stackalloc byte[8];
        Span<byte> chunk = stackalloc byte[8];
        if (source.Read(signature) != 8 || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        while (source.Position + 12 <= source.Length)
        {
            if (source.Read(chunk) != 8) return false;
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]);
            if (length > source.Length - source.Position - 4) return false;
            var isIend = chunk[4..8].SequenceEqual(new byte[] { 0x49, 0x45, 0x4E, 0x44 });
            source.Position += length + 4; // data + CRC
            if (isIend) return length == 0 && source.Position == source.Length;
        }
        return false;
    }
    private static bool IsExactJpeg(Stream source)
    {
        if (source.Length < 4) return false;
        source.Position = 0;
        if (source.ReadByte() != 0xFF || source.ReadByte() != 0xD8) return false;
        var inEntropy = false;
        Span<byte> size = stackalloc byte[2];
        while (source.Position < source.Length)
        {
            var value = source.ReadByte();
            if (value < 0) return false;
            if (value != 0xFF) { if (!inEntropy) return false; continue; }
            do { value = source.ReadByte(); } while (value == 0xFF);
            if (value < 0) return false;
            if (inEntropy && value == 0x00) continue;
            if (value == 0xD9) return source.Position == source.Length;
            if (value is >= 0xD0 and <= 0xD7) continue;
            if (value == 0x01) continue;
            if (source.Read(size) != 2) return false;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(size);
            if (segmentLength < 2 || segmentLength - 2 > source.Length - source.Position) return false;
            if (value == 0xDA) inEntropy = true;
            source.Position += segmentLength - 2;
        }
        return false;
    }
}
