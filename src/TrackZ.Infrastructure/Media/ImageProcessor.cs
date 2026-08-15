using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Media;

public sealed class ImageProcessor : IImageProcessor
{
    private const int MaxPixels = 20_000_000;
    public async Task<ProcessedExerciseImage> ProcessExerciseImageAsync(Stream source, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(source, cancellationToken);
        if ((long)image.Width * image.Height > MaxPixels) throw new InvalidDataException("Image dimensions exceed the processing limit.");
        if (image.Frames.Count != 1) throw new InvalidDataException("Animated images are not accepted.");
        image.Mutate(context => context.AutoOrient());
        var master = await EncodeAsync(image, 1024, cancellationToken);
        var thumbnail = await EncodeAsync(image, 320, cancellationToken);
        return new ProcessedExerciseImage(master, thumbnail, "image/jpeg");
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
