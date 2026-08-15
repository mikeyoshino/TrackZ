using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using TrackZ.Mobile.Features.Exercises.Services;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class LocalExerciseImageImporterSecurityTests
{
    [Theory]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("png", "image/png")]
    [InlineData("webp", "image/webp")]
    public async Task Valid_single_frame_formats_keep_the_original_and_create_a_bounded_preview(
        string format,
        string contentType)
    {
        var root = TemporaryDirectory();
        try
        {
            var bytes = CreateImage(format, 900, 450);
            await using var source = new MemoryStream(bytes);

            var imported = await new LocalExerciseImageImporter().ImportAsync(
                source, contentType, root, "valid-fixture");

            Assert.Equal(bytes, await File.ReadAllBytesAsync(imported.OriginalPath));
            using var preview = ImageSharpImage.Load(imported.PreviewPath);
            Assert.Equal((512, 256), (preview.Width, preview.Height));
            Assert.Single(preview.Frames);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Animated_webp_is_rejected_before_preview_decode()
    {
        var root = TemporaryDirectory();
        try
        {
            using var image = new Image<Rgba32>(40, 20);
            image.Frames.AddFrame(image.Frames.RootFrame);
            image.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = 10;
            image.Frames[1].Metadata.GetWebpMetadata().FrameDelay = 10;
            await using var encoded = new MemoryStream();
            await image.SaveAsync(encoded, new WebpEncoder());
            encoded.Position = 0;

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(encoded, "image/webp", root, "animated"));

            Assert.Contains("Animated", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, "original")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Unsupported_gif_container_is_rejected_even_when_declared_as_png()
    {
        var root = TemporaryDirectory();
        try
        {
            await using var source = new MemoryStream(CreateImage("gif", 20, 10));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(source, "image/png", root, "gif"));

            Assert.False(Directory.Exists(Path.Combine(root, "original")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Small_compressed_webp_with_an_oversized_dimension_is_rejected()
    {
        var root = TemporaryDirectory();
        try
        {
            var bytes = CreateImage("webp", 9_000, 1);
            Assert.True(bytes.Length < 100_000);
            await using var source = new MemoryStream(bytes);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(source, "image/webp", root, "wide-webp"));

            Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Forged_compressed_png_pixel_bomb_is_rejected_from_its_header()
    {
        var root = TemporaryDirectory();
        try
        {
            var bytes = CreateImage("png", 1, 1);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 5_000);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), 5_000);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), Crc32(bytes.AsSpan(12, 17)));
            Assert.True(bytes.Length < 10_000);
            await using var source = new MemoryStream(bytes);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(source, "image/png", root, "pixel-bomb"));

            Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Truncated_supported_container_is_rejected_without_persisting_files()
    {
        var root = TemporaryDirectory();
        try
        {
            var bytes = CreateImage("jpeg", 40, 20);
            await using var source = new MemoryStream(bytes[..(bytes.Length / 2)]);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LocalExerciseImageImporter().ImportAsync(source, "image/jpeg", root, "truncated"));

            Assert.False(Directory.Exists(Path.Combine(root, "original")));
            Assert.False(Directory.Exists(Path.Combine(root, "preview")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-import-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] CreateImage(string format, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var output = new MemoryStream();
        switch (format)
        {
            case "jpeg": image.Save(output, new JpegEncoder()); break;
            case "png": image.Save(output, new PngEncoder()); break;
            case "webp": image.Save(output, new WebpEncoder()); break;
            case "gif": image.Save(output, new GifEncoder()); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
        return output.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
