using System.Buffers.Binary;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Infrastructure.Persistence.Seed;

public sealed class ExerciseCatalogDeploymentService(
    ObjectStorageExerciseCatalogAssetDeployment deployment,
    ExerciseCatalogSeeder seeder)
{
    public async Task DeployAndSeedAsync(string catalogPath, CancellationToken cancellationToken = default)
    {
        await deployment.DeployAsync(catalogPath, cancellationToken);
        await seeder.SeedAsync(catalogPath, cancellationToken);
    }
}

/// <summary>
/// Idempotently installs the exact manifest renditions in private storage. Database metadata is
/// created only after every expected object has been verified byte-for-byte.
/// </summary>
public sealed class ObjectStorageExerciseCatalogAssetDeployment(IObjectStorage storage)
    : IExerciseCatalogAssetDeployment
{
    private const int MaximumDimension = 8192;
    private const int MaximumPixels = 20_000_000;
    private readonly HashSet<Guid> _verifiedInThisRun = [];

    public async Task DeployAsync(string catalogPath, CancellationToken cancellationToken = default)
    {
        _verifiedInThisRun.Clear();
        var items = ExerciseManifest.Load(catalogPath);
        ExerciseManifest.ValidateAssets(catalogPath, items);

        var expected = new List<ExpectedObject>(items.Count * 2);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renditions = await ProcessAsync(
                ExerciseManifest.ResolveAssetPath(catalogPath, item),
                cancellationToken);
            expected.Add(new ExpectedObject(item, ExerciseCatalogSeeder.MasterKey(item.Id), renditions.Master));
            expected.Add(new ExpectedObject(item, ExerciseCatalogSeeder.ThumbnailKey(item.Id), renditions.Thumbnail));
        }

        var missing = new List<ExpectedObject>();
        foreach (var candidate in expected)
        {
            var existing = await storage.GetAsync("system/", candidate.Key, cancellationToken);
            if (existing is null)
            {
                missing.Add(candidate);
                continue;
            }
            if (!await MatchesAsync(existing, candidate.Bytes, cancellationToken))
                throw new InvalidOperationException($"Deployed artwork for '{candidate.Item.Name}' conflicts with the exact catalog rendition.");
        }

        foreach (var candidate in missing)
        {
            await using var content = new MemoryStream(candidate.Bytes, writable: false);
            await storage.PutAsync("system/", candidate.Key, content, "image/png", cancellationToken);
        }

        foreach (var candidate in expected)
        {
            var deployed = await storage.GetAsync("system/", candidate.Key, cancellationToken);
            if (deployed is null || !await MatchesAsync(deployed, candidate.Bytes, cancellationToken))
                throw new InvalidOperationException($"Artwork deployment for '{candidate.Item.Name}' is incomplete or conflicting.");
        }
        foreach (var item in items) _verifiedInThisRun.Add(item.Id);
    }

    public ValueTask<bool> IsDeployedAsync(
        ExerciseManifestItem item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_verifiedInThisRun.Contains(item.Id));
    }

    private static async Task<bool> MatchesAsync(
        ObjectStorageObject value,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        await using (value.Content)
        {
            if (value.Length != expected.LongLength ||
                !string.Equals(value.ContentType, "image/png", StringComparison.Ordinal)) return false;
            var actual = new byte[expected.Length];
            var offset = 0;
            while (offset < actual.Length)
            {
                var read = await value.Content.ReadAsync(actual.AsMemory(offset), cancellationToken);
                if (read == 0) return false;
                offset += read;
            }
            return value.Content.ReadByte() == -1 && actual.AsSpan().SequenceEqual(expected);
        }
    }

    private static async Task<ProcessedArtwork> ProcessAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(sourcePath);
        try
        {
            var format = Image.DetectFormat(source);
            if (format?.DefaultMimeType != "image/png")
                throw new InvalidDataException("Catalog artwork must be PNG.");
            InspectPngBeforeDecode(source);
            source.Position = 0;
            var info = Image.Identify(new DecoderOptions { MaxFrames = 2 }, source)
                ?? throw new InvalidDataException("Catalog artwork is malformed.");
            ValidateDimensions(info.Width, info.Height);
            if (info.FrameMetadataCollection.Count > 1)
                throw new InvalidDataException("Animated catalog artwork is not accepted.");
            source.Position = 0;
            using var image = await Image.LoadAsync(
                new DecoderOptions { MaxFrames = 2, SkipMetadata = false },
                source,
                cancellationToken);
            if (image.Frames.Count != 1)
                throw new InvalidDataException("Animated catalog artwork is not accepted.");
            image.Mutate(context => context.AutoOrient());
            return new ProcessedArtwork(
                await EncodeAsync(image, 1024, cancellationToken),
                await EncodeAsync(image, 320, cancellationToken));
        }
        catch (OperationCanceledException) { throw; }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidDataException("Catalog artwork is malformed.", exception);
        }
    }

    private static void InspectPngBeforeDecode(Stream source)
    {
        source.Position = 0;
        Span<byte> signature = stackalloc byte[8];
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> imageHeader = stackalloc byte[13];
        if (source.Read(signature) != signature.Length ||
            !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Catalog artwork is malformed.");
        var sawHeader = false;
        var sawData = false;
        while (source.Position + 12 <= source.Length)
        {
            if (source.Read(chunkHeader) != chunkHeader.Length)
                throw new InvalidDataException("Catalog artwork is malformed.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            if (length > source.Length - source.Position - 4)
                throw new InvalidDataException("Catalog artwork is malformed.");
            var kind = chunkHeader[4..8];
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || length != 13 || source.Read(imageHeader) != imageHeader.Length)
                    throw new InvalidDataException("Catalog artwork is malformed.");
                ValidateDimensions(
                    BinaryPrimitives.ReadUInt32BigEndian(imageHeader[..4]),
                    BinaryPrimitives.ReadUInt32BigEndian(imageHeader[4..8]));
                sawHeader = true;
                source.Position += 4;
                continue;
            }
            if (!sawHeader)
                throw new InvalidDataException("Catalog artwork is malformed.");
            if (kind.SequenceEqual("acTL"u8) || kind.SequenceEqual("fcTL"u8) || kind.SequenceEqual("fdAT"u8))
                throw new InvalidDataException("Animated catalog artwork is not accepted.");
            if (kind.SequenceEqual("IDAT"u8)) sawData = true;
            var isEnd = kind.SequenceEqual("IEND"u8);
            source.Position += length + 4;
            if (isEnd)
            {
                if (length != 0 || !sawData || source.Position != source.Length)
                    throw new InvalidDataException("Catalog artwork is malformed.");
                return;
            }
        }
        throw new InvalidDataException("Catalog artwork is malformed.");
    }

    private static void ValidateDimensions(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension || width * height > MaximumPixels)
            throw new InvalidDataException("Catalog artwork dimensions exceed the deployment limit.");
    }

    private static async Task<byte[]> EncodeAsync(
        Image source,
        int maximum,
        CancellationToken cancellationToken)
    {
        using var rendition = source.Width <= maximum && source.Height <= maximum
            ? source.Clone(context => { })
            : source.Clone(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maximum, maximum),
                Sampler = KnownResamplers.Lanczos3
            }));
        rendition.Metadata.ExifProfile = null;
        rendition.Metadata.IccProfile = null;
        rendition.Metadata.IptcProfile = null;
        rendition.Metadata.XmpProfile = null;
        await using var output = new MemoryStream();
        await rendition.SaveAsync(output, new PngEncoder(), cancellationToken);
        return output.ToArray();
    }

    private sealed record ExpectedObject(ExerciseManifestItem Item, string Key, byte[] Bytes);
    private sealed record ProcessedArtwork(byte[] Master, byte[] Thumbnail);
}
