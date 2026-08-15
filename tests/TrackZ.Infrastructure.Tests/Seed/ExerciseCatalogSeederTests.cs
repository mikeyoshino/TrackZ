using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence.Seed;
using TrackZ.Infrastructure.Tests.Persistence;

namespace TrackZ.Infrastructure.Tests.Seed;

public sealed class ExerciseCatalogSeederTests
{
    [Fact]
    public async Task Seeder_is_idempotent_and_does_not_create_artwork_before_deployment()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var seeder = new ExerciseCatalogSeeder(database.Db, new UndeployedAssets());

        await seeder.SeedAsync(CatalogPath, default);
        await seeder.SeedAsync(CatalogPath, default);

        var manifest = ExerciseManifest.Load(CatalogPath);
        var exercises = await database.Db.Exercises.OrderBy(exercise => exercise.Name).ToArrayAsync();
        Assert.Equal(48, exercises.Length);
        Assert.Empty(await database.Db.ExerciseImages.ToArrayAsync());
        Assert.Equal(manifest.Select(item => item.Id).Order().ToArray(), exercises.Select(exercise => exercise.Id).Order().ToArray());
        Assert.All(exercises, exercise => Assert.True(exercise.IsSystem));
    }

    [Fact]
    public async Task Seeder_rejects_a_conflicting_existing_name_without_mutating_it()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var manifest = ExerciseManifest.Load(CatalogPath);
        var conflicting = ExerciseDefinition.CreateSystem(manifest[0].Name, BodyPart.Back, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(conflicting);
        await database.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExerciseCatalogSeeder(database.Db, new UndeployedAssets()).SeedAsync(CatalogPath, default));

        Assert.Contains("conflicts", exception.Message, StringComparison.OrdinalIgnoreCase);
        var persisted = await database.Db.Exercises.SingleAsync();
        Assert.Equal(BodyPart.Back, persisted.BodyPart);
        Assert.Equal(conflicting.Id, persisted.Id);
    }

    [Fact]
    public async Task Seeder_rejects_artwork_history_that_lacks_the_manifest_draft_version()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var item = ExerciseManifest.Load(CatalogPath)[0];
        var exercise = ExerciseDefinition.CreateSystem(item.Id, item.Name, item.BodyPart, item.TrackingMode);
        var newerImage = ExerciseImage.CreateSystem(
            exercise,
            "system/exercises/other/v2/master.png",
            "system/exercises/other/v2/thumbnail.png",
            version: 2,
            item.SourceReference);
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.ExerciseImages.AddAsync(newerImage);
        await database.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExerciseCatalogSeeder(database.Db, new DeployedAssets()).SeedAsync(CatalogPath, default));

        Assert.Contains("artwork", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, (await database.Db.ExerciseImages.SingleAsync()).Version);
    }

    [Fact]
    public async Task Seeder_creates_only_draft_system_artwork_when_the_deployment_boundary_confirms_it()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        await new ExerciseCatalogSeeder(database.Db, new DeployedAssets()).SeedAsync(CatalogPath, default);

        var images = await database.Db.ExerciseImages.OrderBy(image => image.ExerciseDefinitionId).ToArrayAsync();
        Assert.Equal(48, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(1, image.Version);
            Assert.Equal(ExerciseImageSource.SystemArtwork, image.Source);
            Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState);
            Assert.Null(image.ReviewedByUserId);
            Assert.Null(image.ReviewedAt);
            Assert.Null(image.PublishedAt);
            Assert.Null(image.RightsReference);
            Assert.False(image.AnatomyApproved);
            Assert.False(image.MovementApproved);
            Assert.False(image.RightsApproved);
            Assert.DoesNotContain("assets/", image.MasterObjectKey, StringComparison.Ordinal);
            Assert.DoesNotContain("assets/", image.ThumbnailObjectKey, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Seeder_preserves_a_valid_reviewed_v1_system_image_on_reseed()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var seeder = new ExerciseCatalogSeeder(database.Db, new DeployedAssets());
        await seeder.SeedAsync(CatalogPath, default);
        var image = await database.Db.ExerciseImages.OrderBy(image => image.ExerciseDefinitionId).FirstAsync();
        var reviewerId = Guid.NewGuid();
        var reviewedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        image.Review(reviewerId, "human-rights-record-001", true, true, true, reviewedAt);
        await database.Db.SaveChangesAsync();
        var snapshot = (image.Id, image.Version, image.MasterObjectKey, image.ThumbnailObjectKey, image.ReviewState, image.ReviewedByUserId, image.ReviewedAt, image.PublishedAt);

        await seeder.SeedAsync(CatalogPath, default);

        var persisted = await database.Db.ExerciseImages.SingleAsync(item => item.Id == image.Id);
        Assert.Equal(snapshot, (persisted.Id, persisted.Version, persisted.MasterObjectKey, persisted.ThumbnailObjectKey, persisted.ReviewState, persisted.ReviewedByUserId, persisted.ReviewedAt, persisted.PublishedAt));
    }

    [Fact]
    public async Task Seeder_preserves_a_valid_published_v1_system_image_on_reseed()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var seeder = new ExerciseCatalogSeeder(database.Db, new DeployedAssets());
        await seeder.SeedAsync(CatalogPath, default);
        var image = await database.Db.ExerciseImages.OrderBy(image => image.ExerciseDefinitionId).FirstAsync();
        var reviewerId = Guid.NewGuid();
        var reviewedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var publishedAt = reviewedAt.AddMinutes(1);
        image.Review(reviewerId, "human-rights-record-002", true, true, true, reviewedAt);
        image.Publish(publishedAt);
        await database.Db.SaveChangesAsync();
        var snapshot = (image.Id, image.Version, image.MasterObjectKey, image.ThumbnailObjectKey, image.ReviewState, image.ReviewedByUserId, image.ReviewedAt, image.PublishedAt);

        await seeder.SeedAsync(CatalogPath, default);

        var persisted = await database.Db.ExerciseImages.SingleAsync(item => item.Id == image.Id);
        Assert.Equal(snapshot, (persisted.Id, persisted.Version, persisted.MasterObjectKey, persisted.ThumbnailObjectKey, persisted.ReviewState, persisted.ReviewedByUserId, persisted.ReviewedAt, persisted.PublishedAt));
    }

    [Fact]
    public async Task Seeder_repeats_deployed_assets_without_changing_image_identity_or_keys()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var seeder = new ExerciseCatalogSeeder(database.Db, new DeployedAssets());
        await seeder.SeedAsync(CatalogPath, default);
        var before = await database.Db.ExerciseImages
            .OrderBy(image => image.ExerciseDefinitionId)
            .Select(image => new { image.Id, image.Version, image.MasterObjectKey, image.ThumbnailObjectKey })
            .ToArrayAsync();

        await seeder.SeedAsync(CatalogPath, default);

        var after = await database.Db.ExerciseImages
            .OrderBy(image => image.ExerciseDefinitionId)
            .Select(image => new { image.Id, image.Version, image.MasterObjectKey, image.ThumbnailObjectKey })
            .ToArrayAsync();
        Assert.Equal(48, after.Length);
        Assert.Equal(before, after);
    }

    private static string CatalogPath => Path.Combine(RepositoryRoot, "assets", "exercises", "catalog.json");

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx"))) directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the TrackZ repository root.");
        }
    }

    private sealed class UndeployedAssets : IExerciseCatalogAssetDeployment
    {
        public ValueTask<bool> IsDeployedAsync(ExerciseManifestItem item, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }

    private sealed class DeployedAssets : IExerciseCatalogAssetDeployment
    {
        public ValueTask<bool> IsDeployedAsync(ExerciseManifestItem item, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
}
