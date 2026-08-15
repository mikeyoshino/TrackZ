using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Media;
public sealed class ImageProcessor : IImageProcessor
{
    private const int MaxPixels = 20_000_000;
    public async Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken)
    {
        if (!source.CanSeek) throw new InvalidDataException("A bounded seekable stream is required.");
        source.Position = 0;
        var format = Image.DetectFormat(source) ?? throw new InvalidDataException("Unknown image format.");
        var detected = format.DefaultMimeType;
        if (detected is not ("image/jpeg" or "image/png" or "image/webp")) throw new InvalidDataException("Unsupported image format.");
        source.Position = 0;
        var info = Image.Identify(new DecoderOptions { MaxFrames = 2 }, source) ?? throw new InvalidDataException("Malformed image.");
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels) throw new InvalidDataException("Image dimensions exceed the processing limit.");
        source.Position = 0;
        using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 2, SkipMetadata = false }, source, cancellationToken);
        if (image.Frames.Count != 1) throw new InvalidDataException("Animated images are not accepted.");
        image.Mutate(context => context.AutoOrient());
        var master = await EncodeAsync(image, 1024, cancellationToken);
        var thumbnail = await EncodeAsync(image, 320, cancellationToken);
        return new ProcessedExerciseImage(master, thumbnail, "image/jpeg", detected);
    }
    private static async Task<byte[]> EncodeAsync(Image image, int maximum, CancellationToken cancellationToken)
    {
        using var copy = image.Clone(context => context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maximum, maximum), Sampler = KnownResamplers.Lanczos3 }));
        copy.Metadata.ExifProfile = null; copy.Metadata.IccProfile = null; copy.Metadata.IptcProfile = null; copy.Metadata.XmpProfile = null;
        await using var output = new MemoryStream();
        await copy.SaveAsync(output, new JpegEncoder { Quality = 85 }, cancellationToken);
        return output.ToArray();
    }
}
