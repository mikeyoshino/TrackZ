using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Exercises;

namespace TrackZ.Infrastructure.Persistence.Seed;

public sealed class ExerciseCatalogPublicationService(
    AppDbContext database,
    ObjectStorageExerciseCatalogAssetDeployment deployment,
    TimeProvider timeProvider)
{
    private const long PublicationLockKey = 0x545241434B5A0006L;

    public async Task PublishAsync(
        string catalogPath,
        Guid reviewerId,
        string rightsReference,
        CancellationToken cancellationToken = default)
    {
        if (reviewerId == Guid.Empty)
            throw new ArgumentException("A reviewer is required.", nameof(reviewerId));
        var normalizedRightsReference = NormalizeRightsReference(rightsReference);

        await deployment.VerifyExactAsync(catalogPath, cancellationToken);
        var manifest = ExerciseManifest.Load(catalogPath);
        var manifestIds = manifest.Select(item => item.Id).ToArray();

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({PublicationLockKey})",
            cancellationToken);
        var definitions = await database.Exercises
            .Where(definition => manifestIds.Contains(definition.Id))
            .OrderBy(definition => definition.Id)
            .ToListAsync(cancellationToken);
        var images = await database.ExerciseImages
            .Where(image => manifestIds.Contains(image.ExerciseDefinitionId))
            .OrderBy(image => image.ExerciseDefinitionId)
            .ToListAsync(cancellationToken);

        if (definitions.Count != manifest.Count || images.Count != manifest.Count)
            throw new InvalidOperationException(
                $"The deployed exercise catalog does not contain exactly {manifest.Count} definitions and images.");

        var definitionsById = definitions.ToDictionary(definition => definition.Id);
        var imagesByDefinition = images
            .GroupBy(image => image.ExerciseDefinitionId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var item in manifest)
        {
            if (!definitionsById.TryGetValue(item.Id, out var definition)
                || !DefinitionMatches(definition, item)
                || !imagesByDefinition.TryGetValue(item.Id, out var definitionImages)
                || definitionImages.Length != 1
                || !ImageMatches(definitionImages[0], item))
            {
                throw new InvalidOperationException($"Exercise catalog publication preflight failed for '{item.Name}'.");
            }
        }

        var draftImages = images.Where(image =>
            image.ReviewState == ExerciseImageReviewState.Draft
            && HasEmptyReviewMetadata(image)).ToArray();
        var publishedImages = images.Where(HasValidPublicationMetadata).ToArray();

        if (draftImages.Length + publishedImages.Length != images.Count)
            throw new InvalidOperationException(
                $"Exercise catalog publication requires every one of the {manifest.Count} exact rows to be Draft or Published.");

        if (draftImages.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var instant = timeProvider.GetUtcNow().ToUniversalTime();
        if (draftImages.Any(image => image.CreatedAt > instant))
            throw new InvalidOperationException("Exercise catalog artwork cannot be reviewed before it was created.");

        foreach (var image in draftImages)
        {
            image.Review(
                reviewerId,
                normalizedRightsReference,
                anatomyApproved: true,
                movementApproved: true,
                rightsApproved: true,
                instant);
            image.Publish(instant);
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool DefinitionMatches(ExerciseDefinition definition, ExerciseManifestItem item) =>
        definition.IsSystem
        && !definition.IsArchived
        && string.Equals(definition.Name, item.Name, StringComparison.Ordinal)
        && string.Equals(definition.NormalizedName, item.Name.ToUpperInvariant(), StringComparison.Ordinal)
        && definition.BodyPart == item.BodyPart
        && definition.TrackingMode == item.TrackingMode;

    private static bool ImageMatches(ExerciseImage image, ExerciseManifestItem item) =>
        image.Source == ExerciseImageSource.SystemArtwork
        && !image.IsPrivate
        && image.OwnerId is null
        && image.Version == 1
        && string.Equals(image.MasterObjectKey, ExerciseCatalogSeeder.MasterKey(item.Id), StringComparison.Ordinal)
        && string.Equals(image.ThumbnailObjectKey, ExerciseCatalogSeeder.ThumbnailKey(item.Id), StringComparison.Ordinal)
        && string.Equals(image.SourceReference, item.SourceReference, StringComparison.Ordinal);

    private static bool HasEmptyReviewMetadata(ExerciseImage image) =>
        image.ReviewedByUserId is null
        && image.ReviewedAt is null
        && image.PublishedAt is null
        && image.RightsReference is null
        && !image.AnatomyApproved
        && !image.MovementApproved
        && !image.RightsApproved
        && !image.IsReadyForUse;

    private static bool HasValidPublicationMetadata(ExerciseImage image) =>
        image.ReviewState == ExerciseImageReviewState.Published
        && image.ReviewedByUserId is { } reviewerId
        && reviewerId != Guid.Empty
        && !string.IsNullOrWhiteSpace(image.RightsReference)
        && image.AnatomyApproved
        && image.MovementApproved
        && image.RightsApproved
        && image.ReviewedAt is { } reviewedAt
        && image.PublishedAt is { } publishedAt
        && reviewedAt >= image.CreatedAt
        && publishedAt >= reviewedAt
        && image.IsReadyForUse;

    private static string NormalizeRightsReference(string rightsReference)
    {
        var normalized = rightsReference?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 512)
            throw new ArgumentOutOfRangeException(nameof(rightsReference), "A rights reference between 1 and 512 characters is required.");
        return normalized;
    }
}
