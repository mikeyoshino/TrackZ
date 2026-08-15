using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using TrackZ.Infrastructure.Media;

namespace TrackZ.Infrastructure.Tests.Media;

public sealed class ImageProcessorTests
{
    [Fact]
    public async Task Png_is_detected_and_normalized_to_metadata_free_bounded_jpegs()
    {
        await using var source = new MemoryStream();
        using (var image = new Image<Rgba32>(1600, 800)) await image.SaveAsync(source, new PngEncoder());
        var result = await new ImageProcessor().ProcessExerciseImageAsync(source, default);

        Assert.Equal("image/png", result.DetectedContentType);
        Assert.Equal("image/jpeg", result.ContentType);
        using var master = Image.Load(result.Master);
        using var thumbnail = Image.Load(result.Thumbnail);
        Assert.True(master.Width <= 1024 && master.Height <= 1024);
        Assert.True(thumbnail.Width <= 320 && thumbnail.Height <= 320);
        Assert.Equal(2d, master.Width / (double)master.Height, 2);
        Assert.Single(master.Frames);
        Assert.Null(master.Metadata.ExifProfile);
        Assert.Null(master.Metadata.XmpProfile);
        Assert.Null(master.Metadata.IptcProfile);
        Assert.Null(master.Metadata.IccProfile);
    }

    [Fact]
    public async Task Non_seekable_and_non_supported_images_are_rejected()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(new NonSeekableStream(), default));
        await using var bytes = new MemoryStream([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(bytes, default));
    }

    private sealed class NonSeekableStream : MemoryStream
    { public override bool CanSeek => false; }
}
