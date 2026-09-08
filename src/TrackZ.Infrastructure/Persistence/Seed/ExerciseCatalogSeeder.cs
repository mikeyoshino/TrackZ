using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Exercises;

namespace TrackZ.Infrastructure.Persistence.Seed;

/// <summary>
/// Confirms that a catalog asset has crossed the deployment boundary into private object storage.
/// A local repository file is not confirmation and must never be exposed as an API URL.
/// </summary>
public interface IExerciseCatalogAssetDeployment
{
    ValueTask<bool> IsDeployedAsync(ExerciseManifestItem item, CancellationToken cancellationToken);
}

public sealed class ExerciseCatalogSeeder(AppDbContext database, IExerciseCatalogAssetDeployment assetDeployment)
{
    public async Task SeedAsync(string catalogPath, CancellationToken cancellationToken = default)
    {
        var items = ExerciseManifest.Load(catalogPath);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var supplemental = await database.Exercises.SingleOrDefaultAsync(e =>
            e.Id == SupplementalExercises.SeatedBarbellShoulderPressId, cancellationToken);
        if (supplemental is null)
        {
            if (await database.Exercises.AnyAsync(e => e.OwnerId == null &&
                e.NormalizedName == SupplementalExercises.SeatedBarbellShoulderPressName.ToUpperInvariant(), cancellationToken))
                throw new InvalidOperationException("Supplemental shoulder press conflicts with an existing system definition.");
            database.Exercises.Add(ExerciseDefinition.CreateSystem(SupplementalExercises.SeatedBarbellShoulderPressId,
                SupplementalExercises.SeatedBarbellShoulderPressName, BodyPart.Shoulders, TrackingMode.Weighted));
        }
        else if (!supplemental.IsSystem || supplemental.IsArchived || supplemental.Name != SupplementalExercises.SeatedBarbellShoulderPressName
            || supplemental.BodyPart != BodyPart.Shoulders || supplemental.TrackingMode != TrackingMode.Weighted)
            throw new InvalidOperationException("Supplemental shoulder press definition is inconsistent.");

        var ids = items.Select(item => item.Id).ToArray();
        var normalizedNames = items.Select(item => item.Name.ToUpperInvariant()).ToArray();
        var existingById = await database.Exercises
            .Where(exercise => ids.Contains(exercise.Id))
            .ToDictionaryAsync(exercise => exercise.Id, cancellationToken);
        var existingByName = await database.Exercises
            .Where(exercise => exercise.OwnerId == null
                && normalizedNames.Contains(exercise.NormalizedName))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            if (existingById.TryGetValue(item.Id, out var existing))
            {
                EnsureMatchingDefinition(existing, item);
            }

            if (existingByName.Any(exercise =>
                    string.Equals(exercise.NormalizedName, item.Name.ToUpperInvariant(), StringComparison.Ordinal)
                    && exercise.Id != item.Id))
            {
                throw new InvalidOperationException($"Exercise catalog name '{item.Name}' conflicts with an existing exercise.");
            }

            if (!existingById.ContainsKey(item.Id))
            {
                await database.Exercises.AddAsync(
                    ExerciseDefinition.CreateSystem(item.Id, item.Name, item.BodyPart, item.TrackingMode),
                    cancellationToken);
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        var existingImages = await database.ExerciseImages
            .Where(image => ids.Contains(image.ExerciseDefinitionId))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var image = existingImages.SingleOrDefault(existing => existing.ExerciseDefinitionId == item.Id && existing.Version == 1);
            if (image is not null)
            {
                EnsureMatchingSystemImage(image, item);
                continue;
            }

            if (existingImages.Any(existing => existing.ExerciseDefinitionId == item.Id))
            {
                throw new InvalidOperationException($"Exercise catalog artwork for '{item.Name}' conflicts with the Draft manifest.");
            }

            if (!await assetDeployment.IsDeployedAsync(item, cancellationToken))
            {
                continue;
            }

            var exercise = existingById.TryGetValue(item.Id, out var existing)
                ? existing
                : database.Exercises.Local.Single(definition => definition.Id == item.Id);
            await database.ExerciseImages.AddAsync(ExerciseImage.CreateSystem(
                exercise,
                MasterKey(item.Id),
                ThumbnailKey(item.Id),
                version: 1,
                item.SourceReference), cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static string MasterKey(Guid exerciseId) => $"system/exercises/{exerciseId:N}/v1/master.png";

    public static string ThumbnailKey(Guid exerciseId) => $"system/exercises/{exerciseId:N}/v1/thumbnail.png";

    private static void EnsureMatchingDefinition(ExerciseDefinition existing, ExerciseManifestItem item)
    {
        if (!existing.IsSystem
            || existing.IsArchived
            || !string.Equals(existing.Name, item.Name, StringComparison.Ordinal)
            || existing.BodyPart != item.BodyPart
            || existing.TrackingMode != item.TrackingMode)
        {
            throw new InvalidOperationException($"Exercise catalog ID '{item.Id:D}' conflicts with an existing exercise.");
        }
    }

    private static void EnsureMatchingSystemImage(ExerciseImage image, ExerciseManifestItem item)
    {
        if (image.Source != ExerciseImageSource.SystemArtwork
            || image.IsPrivate
            || image.OwnerId is not null
            || image.Version != 1
            || !string.Equals(image.MasterObjectKey, MasterKey(item.Id), StringComparison.Ordinal)
            || !string.Equals(image.ThumbnailObjectKey, ThumbnailKey(item.Id), StringComparison.Ordinal)
            || !string.Equals(image.SourceReference, item.SourceReference, StringComparison.Ordinal)
            || !HasConsistentLifecycleMetadata(image))
        {
            throw new InvalidOperationException($"Exercise catalog artwork for '{item.Name}' conflicts with the Draft manifest.");
        }
    }

    private static bool HasConsistentLifecycleMetadata(ExerciseImage image) => image.ReviewState switch
    {
        ExerciseImageReviewState.Draft =>
            image.ReviewedByUserId is null
            && image.ReviewedAt is null
            && image.PublishedAt is null
            && image.RightsReference is null
            && !image.AnatomyApproved
            && !image.MovementApproved
            && !image.RightsApproved,
        ExerciseImageReviewState.Reviewed =>
            image.ReviewedByUserId is not null
            && image.ReviewedAt is not null
            && image.PublishedAt is null
            && !string.IsNullOrWhiteSpace(image.RightsReference)
            && image.AnatomyApproved
            && image.MovementApproved
            && image.RightsApproved
            && image.ReviewedAt >= image.CreatedAt,
        ExerciseImageReviewState.Published =>
            image.ReviewedByUserId is not null
            && image.ReviewedAt is not null
            && image.PublishedAt is not null
            && !string.IsNullOrWhiteSpace(image.RightsReference)
            && image.AnatomyApproved
            && image.MovementApproved
            && image.RightsApproved
            && image.ReviewedAt >= image.CreatedAt
            && image.PublishedAt >= image.ReviewedAt,
        _ => false
    };
}
