using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using TrackZ.Infrastructure.Media;

namespace TrackZ.Infrastructure.Tests.Media;

public sealed class ImageProcessorTests
{
    [Theory]
    [InlineData(ImageKind.Jpeg, "image/jpeg")]
    [InlineData(ImageKind.Png, "image/png")]
    [InlineData(ImageKind.Webp, "image/webp")]
    public async Task Supported_real_formats_are_detected_and_reencoded_as_jpeg(ImageKind kind, string expectedContentType)
    {
        await using var source = new MemoryStream(CreateImage(kind, 1500, 999));

        var result = await new ImageProcessor().ProcessExerciseImageAsync(source, default);

        Assert.Equal(expectedContentType, result.DetectedContentType);
        Assert.Equal("image/jpeg", result.ContentType);
        using var master = Image.Load(result.Master);
        using var thumbnail = Image.Load(result.Thumbnail);
        Assert.Equal(1024, master.Width);
        Assert.Equal(682, master.Height);
        Assert.Equal(320, thumbnail.Width);
        Assert.Equal(213, thumbnail.Height);
        Assert.InRange(Math.Abs(master.Width / (double)master.Height - 1500d / 999d), 0, .002);
        Assert.InRange(Math.Abs(thumbnail.Width / (double)thumbnail.Height - 1500d / 999d), 0, .006);
        Assert.Single(master.Frames);
        Assert.Single(thumbnail.Frames);
    }

    [Theory]
    [InlineData(ImageKind.Gif)]
    [InlineData(ImageKind.Bmp)]
    [InlineData(ImageKind.Tiff)]
    public async Task Single_frame_unsupported_formats_are_rejected(ImageKind kind)
    {
        await using var source = new MemoryStream(CreateImage(kind, 20, 10));

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));
    }

    [Fact]
    public async Task Truncated_and_malformed_payloads_are_rejected()
    {
        var jpeg = CreateImage(ImageKind.Jpeg, 50, 40);
        await using var truncated = new MemoryStream(jpeg[..(jpeg.Length / 2)]);
        await using var malformed = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0, 0, 0]);
        var processor = new ImageProcessor();

        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessExerciseImageAsync(truncated, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ProcessExerciseImageAsync(malformed, default));
    }

    [Fact]
    public async Task Forged_terminal_jpeg_payload_is_rejected()
    {
        var bytes = CreateImage(ImageKind.Jpeg, 20, 10).Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xFF, 0xD9 }).ToArray();
        await using var source = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));
    }

    [Fact]
    public async Task Forged_terminal_png_payload_is_rejected()
    {
        var bytes = CreateImage(ImageKind.Png, 20, 10).Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 }).ToArray();
        await using var source = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));
    }

    [Fact]
    public async Task Riff_length_consistent_unknown_webp_chunk_is_rejected()
    {
        var bytes = AppendWebpChunk(CreateImage(ImageKind.Webp, 20, 10), "JUNK", new byte[] { 1, 2, 3, 4 });
        Assert.Equal((uint)(bytes.Length - 8), BitConverter.ToUInt32(bytes, 4)); // the old RIFF-length-only check accepted this
        await using var source = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));
    }

    [Fact]
    public async Task Standard_webp_xmp_metadata_is_accepted_and_removed_from_jpeg_renditions()
    {
        var sourceBytes = AppendWebpChunk(CreateImage(ImageKind.Webp, 40, 20), "XMP ", Encoding.UTF8.GetBytes("<x:xmpmeta>webp-secret</x:xmpmeta>"));
        await using var source = new MemoryStream(sourceBytes);

        var result = await new ImageProcessor().ProcessExerciseImageAsync(source, default);

        using var master = Image.Load(result.Master);
        using var thumbnail = Image.Load(result.Thumbnail);
        Assert.Null(master.Metadata.ExifProfile);
        Assert.Null(master.Metadata.XmpProfile);
        Assert.Null(master.Metadata.IptcProfile);
        Assert.Null(master.Metadata.IccProfile);
        Assert.Null(thumbnail.Metadata.ExifProfile);
        Assert.Null(thumbnail.Metadata.XmpProfile);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("webp-secret"), result.Master);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("webp-secret"), result.Thumbnail);
    }

    [Fact]
    public async Task Multi_frame_webp_is_rejected()
    {
        using var image = new Image<Rgba32>(40, 20);
        image.Frames.AddFrame(image.Frames.RootFrame);
        image.Metadata.GetWebpMetadata().RepeatCount = 0;
        image.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay = 10;
        image.Frames[1].Metadata.GetWebpMetadata().FrameDelay = 10;
        await using var source = new MemoryStream();
        await image.SaveAsync(source, new WebpEncoder());
        source.Position = 0;
        using (var fixture = Image.Load(source)) Assert.Equal(2, fixture.Frames.Count);
        source.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));
    }

    [Fact]
    public async Task Oversized_pixel_header_is_rejected_during_identification_before_full_payload_read()
    {
        var png = CreateImage(ImageKind.Png, 1, 1);
        WriteBigEndian(png, 16, 5_000);
        WriteBigEndian(png, 20, 5_000);
        WriteBigEndian(png, 29, Crc32(png.AsSpan(12, 17)));
        var payload = png[..^12].Concat(new byte[1_000_000]).Concat(png[^12..]).ToArray();
        await using var source = new CountingStream(payload);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));

        Assert.True(source.BytesRead < 100_000, $"Identify read {source.BytesRead} bytes from a {payload.Length}-byte payload.");
    }

    [Theory]
    [InlineData(20_000_000, 1)]
    [InlineData(1, 20_000_000)]
    public async Task Extreme_png_header_dimensions_are_rejected_before_decode(uint width, uint height)
    {
        var png = CreateImage(ImageKind.Png, 1, 1);
        WriteBigEndian(png, 16, width);
        WriteBigEndian(png, 20, height);
        WriteBigEndian(png, 29, Crc32(png.AsSpan(12, 17)));
        await using var source = new CountingStream(png);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, default));
        Assert.True(source.BytesRead < 100_000);
    }

    [Fact]
    public async Task Orientation_and_sensitive_metadata_are_removed_from_normalized_jpegs()
    {
        var bytes = CreateJpegWithSensitiveMetadata();
        await using var source = new MemoryStream(bytes);

        var result = await new ImageProcessor().ProcessExerciseImageAsync(source, default);

        using var master = Image.Load(result.Master);
        Assert.Equal(80, master.Width);
        Assert.Equal(40, master.Height);
        Assert.Null(master.Metadata.ExifProfile);
        Assert.Null(master.Metadata.XmpProfile);
        Assert.Null(master.Metadata.IptcProfile);
        Assert.Null(master.Metadata.IccProfile);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("sensitive-comment"), result.Master);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("secret-xmp"), result.Master);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("secret-iptc"), result.Master);
    }

    [Fact]
    public async Task Small_inputs_are_not_upscaled()
    {
        await using var source = new MemoryStream(CreateImage(ImageKind.Jpeg, 100, 50));

        var result = await new ImageProcessor().ProcessExerciseImageAsync(source, default);

        using var master = Image.Load(result.Master);
        using var thumbnail = Image.Load(result.Thumbnail);
        Assert.Equal((100, 50), (master.Width, master.Height));
        Assert.Equal((100, 50), (thumbnail.Width, thumbnail.Height));
    }

    [Fact]
    public async Task Cancellation_is_observed_before_processing_the_source()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var source = new MemoryStream(CreateImage(ImageKind.Jpeg, 20, 10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImageProcessor().ProcessExerciseImageAsync(source, cancellation.Token));
    }

    [Fact]
    public async Task Nonseekable_sources_are_rejected_before_image_parsing()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageProcessor().ProcessExerciseImageAsync(new NonSeekableStream(), default));
    }

    private static byte[] CreateImage(ImageKind kind, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var output = new MemoryStream();
        switch (kind)
        {
            case ImageKind.Jpeg: image.Save(output, new JpegEncoder()); break;
            case ImageKind.Png: image.Save(output, new PngEncoder()); break;
            case ImageKind.Webp: image.Save(output, new WebpEncoder()); break;
            case ImageKind.Gif: image.Save(output, new GifEncoder()); break;
            case ImageKind.Bmp: image.Save(output, new BmpEncoder()); break;
            case ImageKind.Tiff: image.Save(output, new TiffEncoder()); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return output.ToArray();
    }

    private static byte[] CreateJpegWithSensitiveMetadata()
    {
        using var image = new Image<Rgba32>(40, 80);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        image.Metadata.ExifProfile = exif;
        image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta>secret-xmp</x:xmpmeta>"));
        image.Metadata.IccProfile = new IccProfile();
        var iptc = new IptcProfile();
        iptc.SetValue(IptcTag.Caption, "secret-iptc");
        image.Metadata.IptcProfile = iptc;
        using var output = new MemoryStream();
        image.Save(output, new JpegEncoder());
        var commented = InsertJpegComment(output.ToArray(), "sensitive-comment");
        using var fixture = Image.Load(commented);
        Assert.NotNull(fixture.Metadata.ExifProfile);
        Assert.NotNull(fixture.Metadata.XmpProfile);
        Assert.NotNull(fixture.Metadata.IptcProfile);
        Assert.Contains(Encoding.UTF8.GetBytes("sensitive-comment"), commented);
        return commented;
    }

    private static byte[] AppendWebpChunk(byte[] webp, string chunkType, byte[] payload)
    {
        Assert.Equal(4, chunkType.Length);
        var padded = payload.Length + (payload.Length & 1);
        var result = new byte[webp.Length + 8 + padded];
        Buffer.BlockCopy(webp, 0, result, 0, webp.Length);
        Encoding.ASCII.GetBytes(chunkType).CopyTo(result, webp.Length);
        BitConverter.GetBytes(payload.Length).CopyTo(result, webp.Length + 4);
        Buffer.BlockCopy(payload, 0, result, webp.Length + 8, payload.Length);
        BitConverter.GetBytes(result.Length - 8).CopyTo(result, 4);
        return result;
    }

    private static byte[] InsertJpegComment(byte[] jpeg, string comment)
    {
        var payload = Encoding.UTF8.GetBytes(comment);
        var result = new byte[jpeg.Length + payload.Length + 4];
        result[0] = jpeg[0]; result[1] = jpeg[1]; result[2] = 0xFF; result[3] = 0xFE;
        result[4] = (byte)((payload.Length + 2) >> 8); result[5] = (byte)(payload.Length + 2);
        payload.CopyTo(result, 6);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(payload.Length + 6));
        return result;
    }

    private static void WriteBigEndian(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24); data[offset + 1] = (byte)(value >> 16); data[offset + 2] = (byte)(value >> 8); data[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        return ~crc;
    }

    public enum ImageKind { Jpeg, Png, Webp, Gif, Bmp, Tiff }

    private sealed class NonSeekableStream : MemoryStream { public override bool CanSeek => false; }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) { var read = base.Read(buffer, offset, count); BytesRead += read; return read; }
        public override int Read(Span<byte> buffer) { var read = base.Read(buffer); BytesRead += read; return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { var read = await base.ReadAsync(buffer, cancellationToken); BytesRead += read; return read; }
    }
}
