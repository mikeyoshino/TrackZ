using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Persistence.Seed;
using TrackZ.Infrastructure.Tests.Persistence;

namespace TrackZ.Infrastructure.Tests.Seed;

[Collection(ExerciseCatalogPublicationCollection.Name)]
public sealed class ExerciseCatalogPublicationTests(ExerciseCatalogPublicationFixture fixture)
{
    private static readonly Guid ReviewerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherReviewerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string RightsReference = "local-simulator-review-2026-08-21";

    [Fact]
    public async Task Exact_48_drafts_publish_atomically_with_complete_review_metadata()
    {
        var scenario = await fixture.PrepareAsync();

        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);

        var images = await scenario.Database.ExerciseImages.AsNoTracking()
            .OrderBy(image => image.ExerciseDefinitionId)
            .ToArrayAsync();
        Assert.Equal(48, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
            Assert.True(image.AnatomyApproved && image.MovementApproved && image.RightsApproved);
            Assert.True(image.IsReadyForUse);
            Assert.Equal(scenario.Now, image.ReviewedAt);
            Assert.Equal(scenario.Now, image.PublishedAt);
        });
    }

    [Fact]
    public async Task Verification_reads_exact_96_private_objects_without_writes()
    {
        var scenario = await fixture.PrepareAsync();
        scenario.Storage.ResetCounters();

        await scenario.Deployment.VerifyExactAsync(CatalogPath);

        Assert.Equal(96, scenario.Storage.GetCount);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
        var expectedKeys = ExerciseManifest.Load(CatalogPath)
            .SelectMany(item => new[]
            {
                ExerciseCatalogSeeder.MasterKey(item.Id),
                ExerciseCatalogSeeder.ThumbnailKey(item.Id)
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedKeys, scenario.Storage.ReadKeys.Order(StringComparer.Ordinal));
        Assert.All(scenario.Storage.ReadPrefixes, prefix => Assert.Equal("system/", prefix));
    }

    [Fact]
    public async Task Publication_fails_closed_when_one_object_is_missing()
    {
        var scenario = await fixture.PrepareAsync();
        var first = ExerciseManifest.Load(CatalogPath)[0];
        scenario.Storage.Remove(ExerciseCatalogSeeder.MasterKey(first.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        await AssertAllDraftAsync(scenario.Database);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_fails_closed_for_one_byte_or_content_type_mismatch(bool contentTypeMismatch)
    {
        var scenario = await fixture.PrepareAsync();
        var first = ExerciseManifest.Load(CatalogPath)[0];
        var key = ExerciseCatalogSeeder.ThumbnailKey(first.Id);
        if (contentTypeMismatch)
            scenario.Storage.ChangeContentType(key, "image/jpeg");
        else
            scenario.Storage.CorruptOneByte(key);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        await AssertAllDraftAsync(scenario.Database);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
    }

    [Fact]
    public async Task Publication_rejects_only_47_image_rows_before_any_state_change()
    {
        var scenario = await fixture.PrepareAsync();
        var firstId = await scenario.Database.ExerciseImages.Select(image => image.Id).FirstAsync();
        await scenario.Database.ExerciseImages.Where(image => image.Id == firstId).ExecuteDeleteAsync();
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        var remaining = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(47, remaining.Length);
        Assert.All(remaining, AssertDraft);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("master-key")]
    [InlineData("thumbnail-key")]
    [InlineData("source-reference")]
    public async Task Publication_rejects_unexpected_image_identity_metadata(string mutation)
    {
        var scenario = await fixture.PrepareAsync();
        var image = await scenario.Database.ExerciseImages.AsNoTracking().OrderBy(item => item.Id).FirstAsync();
        switch (mutation)
        {
            case "version":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.Version, 2));
                break;
            case "master-key":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.MasterObjectKey, "system/unexpected/master.png"));
                break;
            case "thumbnail-key":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.ThumbnailObjectKey, "system/unexpected/thumbnail.png"));
                break;
            case "source-reference":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.SourceReference, "unexpected-source"));
                break;
        }
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        Assert.All(await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync(), AssertDraft);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("body-part")]
    [InlineData("tracking-mode")]
    public async Task Publication_rejects_unexpected_definition_metadata(string mutation)
    {
        var scenario = await fixture.PrepareAsync();
        var definition = await scenario.Database.Exercises.AsNoTracking().OrderBy(item => item.Id).FirstAsync();
        switch (mutation)
        {
            case "name":
                await scenario.Database.Exercises.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.Name, "Unexpected Exercise")
                        .SetProperty(item => item.NormalizedName, "UNEXPECTED EXERCISE"));
                break;
            case "body-part":
                var differentBodyPart = definition.BodyPart == BodyPart.Arms ? BodyPart.Back : BodyPart.Arms;
                await scenario.Database.Exercises.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.BodyPart, differentBodyPart));
                break;
            case "tracking-mode":
                var differentMode = definition.TrackingMode == TrackingMode.Weighted
                    ? TrackingMode.Bodyweight
                    : TrackingMode.Weighted;
                await scenario.Database.Exercises.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.TrackingMode, differentMode));
                break;
        }
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        Assert.All(await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync(), AssertDraft);
    }

    [Fact]
    public async Task Publication_rejects_a_reviewed_row_without_publishing_any_row()
    {
        var scenario = await fixture.PrepareAsync();
        var first = await scenario.Database.ExerciseImages.OrderBy(image => image.Id).FirstAsync();
        first.Review(ReviewerId, RightsReference, true, true, true, scenario.Now);
        await scenario.Database.SaveChangesAsync();
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        var images = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Single(images, image => image.ReviewState == ExerciseImageReviewState.Reviewed);
        Assert.DoesNotContain(images, image => image.ReviewState == ExerciseImageReviewState.Published);
    }

    [Fact]
    public async Task Publication_preflights_all_created_timestamps_before_mutating_tracked_drafts()
    {
        var scenario = await fixture.PrepareAsync();
        var lastId = await scenario.Database.ExerciseImages.AsNoTracking()
            .OrderBy(image => image.ExerciseDefinitionId)
            .Select(image => image.Id)
            .LastAsync();
        await scenario.Database.ExerciseImages.Where(image => image.Id == lastId)
            .ExecuteUpdateAsync(update => update.SetProperty(image => image.CreatedAt, scenario.Now.AddDays(1)));
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        Assert.All(scenario.Database.ChangeTracker.Entries<ExerciseImage>(), entry => AssertDraft(entry.Entity));
        await AssertAllDraftAsync(scenario.Database);
    }

    [Fact]
    public async Task Publication_rejects_mixed_draft_and_published_rows_without_mutation()
    {
        var scenario = await fixture.PrepareAsync();
        var first = await scenario.Database.ExerciseImages.OrderBy(image => image.Id).FirstAsync();
        first.Review(ReviewerId, RightsReference, true, true, true, scenario.Now);
        first.Publish(scenario.Now);
        await scenario.Database.SaveChangesAsync();
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        var images = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Single(images, image => image.ReviewState == ExerciseImageReviewState.Published);
        Assert.Equal(47, images.Count(image => image.ReviewState == ExerciseImageReviewState.Draft));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Published_rerun_rejects_conflicting_reviewer_or_rights(bool conflictReviewer)
    {
        var scenario = await fixture.PrepareAsync();
        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.Publication.PublishAsync(
            CatalogPath,
            conflictReviewer ? OtherReviewerId : ReviewerId,
            conflictReviewer ? RightsReference : "different-rights-reference"));

        var images = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.All(images, image =>
        {
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
            Assert.Equal(scenario.Now, image.ReviewedAt);
            Assert.Equal(scenario.Now, image.PublishedAt);
        });
    }

    [Fact]
    public async Task Exact_published_rerun_is_idempotent_with_unchanged_timestamps()
    {
        var scenario = await fixture.PrepareAsync();
        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, $"  {RightsReference}  ");
        scenario.Database.ChangeTracker.Clear();
        var before = await scenario.Database.ExerciseImages.AsNoTracking()
            .ToDictionaryAsync(image => image.Id, image => new { image.ReviewedAt, image.PublishedAt });
        scenario.Time.Advance(TimeSpan.FromDays(2));

        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);

        var after = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.All(after, image =>
        {
            Assert.Equal(before[image.Id].ReviewedAt, image.ReviewedAt);
            Assert.Equal(before[image.Id].PublishedAt, image.PublishedAt);
            Assert.Equal(RightsReference, image.RightsReference);
        });
    }

    [Fact]
    public async Task Trigger_failure_on_24th_publish_update_rolls_back_all_48_rows()
    {
        var scenario = await fixture.PrepareAsync();
        await InstallFailureTriggerAsync(scenario.Database);
        try
        {
            await using (var publicationContext = fixture.Database.CreateDbContext())
            {
                var publication = new ExerciseCatalogPublicationService(
                    publicationContext,
                    scenario.Deployment,
                    scenario.Time);

                var error = await Assert.ThrowsAsync<DbUpdateException>(() => publication.PublishAsync(
                    CatalogPath,
                    ReviewerId,
                    RightsReference));
                Assert.Contains("injected catalog publication failure on update 24", error.ToString(), StringComparison.Ordinal);
            }

            await using var verificationContext = fixture.Database.CreateDbContext();
            var images = await verificationContext.ExerciseImages.AsNoTracking().ToArrayAsync();
            Assert.Equal(48, images.Length);
            Assert.All(images, AssertDraft);
        }
        finally
        {
            await DropFailureTriggerAsync(scenario.Database);
        }
    }

    private static async Task AssertAllDraftAsync(AppDbContext database)
    {
        database.ChangeTracker.Clear();
        var images = await database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(48, images.Length);
        Assert.All(images, AssertDraft);
    }

    private static void AssertDraft(ExerciseImage image)
    {
        Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState);
        Assert.Null(image.ReviewedByUserId);
        Assert.Null(image.ReviewedAt);
        Assert.Null(image.PublishedAt);
        Assert.Null(image.RightsReference);
        Assert.False(image.AnatomyApproved);
        Assert.False(image.MovementApproved);
        Assert.False(image.RightsApproved);
        Assert.False(image.IsReadyForUse);
    }

    private static async Task InstallFailureTriggerAsync(AppDbContext database)
    {
        await DropFailureTriggerAsync(database);
        await database.Database.ExecuteSqlRawAsync(
            "CREATE TABLE catalog_publication_trigger_counter (value integer NOT NULL)");
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO catalog_publication_trigger_counter (value) VALUES (0)");
        await database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_catalog_publication_on_24th_update() RETURNS trigger AS $$
            DECLARE update_count integer;
            BEGIN
                UPDATE catalog_publication_trigger_counter
                SET value = value + 1
                RETURNING value INTO update_count;
                IF update_count = 24 THEN
                    RAISE EXCEPTION 'injected catalog publication failure on update 24';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql
            """);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_catalog_publication_on_24th_update
            BEFORE UPDATE OF "ReviewState" ON exercise_images
            FOR EACH ROW
            WHEN (NEW."ReviewState" = 3 AND OLD."ReviewState" IS DISTINCT FROM NEW."ReviewState")
            EXECUTE FUNCTION fail_catalog_publication_on_24th_update()
            """);
    }

    private static async Task DropFailureTriggerAsync(AppDbContext database)
    {
        database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS fail_catalog_publication_on_24th_update ON exercise_images");
        await database.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS fail_catalog_publication_on_24th_update()");
        await database.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS catalog_publication_trigger_counter");
    }

    private static string CatalogPath => ExerciseCatalogPublicationFixture.CatalogPath;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExerciseCatalogPublicationCollection : ICollectionFixture<ExerciseCatalogPublicationFixture>
{
    public const string Name = "Exercise catalog publication";
}

public sealed class ExerciseCatalogPublicationFixture : IAsyncLifetime
{
    private PostgreSqlFixture? _database;

    internal PostgreSqlFixture Database => _database
        ?? throw new InvalidOperationException("The PostgreSQL fixture has not started.");

    public async Task InitializeAsync() => _database = await PostgreSqlFixture.StartAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null) await _database.DisposeAsync();
    }

    internal async Task<PublicationScenario> PrepareAsync()
    {
        var database = Database.Db;
        database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS fail_catalog_publication_on_24th_update ON exercise_images");
        await database.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS fail_catalog_publication_on_24th_update()");
        await database.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS catalog_publication_trigger_counter");
        await database.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE exercise_images, exercise_definitions CASCADE");

        var storage = new InspectableObjectStorage();
        var deployment = new ObjectStorageExerciseCatalogAssetDeployment(storage);
        var deployAndSeed = new ExerciseCatalogDeploymentService(
            deployment,
            new ExerciseCatalogSeeder(database, deployment));
        await deployAndSeed.DeployAndSeedAsync(CatalogPath);
        storage.ResetCounters();
        database.ChangeTracker.Clear();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        var publication = new ExerciseCatalogPublicationService(database, deployment, time);
        return new PublicationScenario(database, storage, deployment, publication, time);
    }

    internal static string CatalogPath => Path.Combine(RepositoryRoot, "assets", "exercises", "catalog.json");

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the TrackZ repository root.");
        }
    }
}

internal sealed record PublicationScenario(
    AppDbContext Database,
    InspectableObjectStorage Storage,
    ObjectStorageExerciseCatalogAssetDeployment Deployment,
    ExerciseCatalogPublicationService Publication,
    MutableTimeProvider Time)
{
    public DateTimeOffset Now => Time.GetUtcNow();
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

internal sealed class InspectableObjectStorage : IObjectStorage
{
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);
    private int _putCountAtReset;
    private int _deleteCountAtReset;

    public int GetCount { get; private set; }
    public int PutCount { get; private set; }
    public int DeleteCount { get; private set; }
    public int PutCountAfterReset => PutCount - _putCountAtReset;
    public int DeleteCountAfterReset => DeleteCount - _deleteCountAtReset;
    public List<string> ReadKeys { get; } = [];
    public List<string> ReadPrefixes { get; } = [];

    public Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetCount++;
        ReadKeys.Add(key);
        ReadPrefixes.Add(ownerPrefix);
        return Task.FromResult(_objects.TryGetValue(key, out var value)
            ? new ObjectStorageObject(
                value.Bytes.LongLength,
                value.ContentType,
                new MemoryStream(value.Bytes, writable: false))
            : null);
    }

    public async Task PutAsync(
        string ownerPrefix,
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        PutCount++;
        await using var output = new MemoryStream();
        await content.CopyToAsync(output, cancellationToken);
        _objects[key] = new StoredObject(output.ToArray(), contentType);
    }

    public Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken)
    {
        DeleteCount++;
        _objects.Remove(key);
        return Task.CompletedTask;
    }

    public void ResetCounters()
    {
        GetCount = 0;
        _putCountAtReset = PutCount;
        _deleteCountAtReset = DeleteCount;
        ReadKeys.Clear();
        ReadPrefixes.Clear();
    }

    public void Remove(string key) => _objects.Remove(key);

    public void CorruptOneByte(string key)
    {
        var value = _objects[key];
        var corrupted = value.Bytes.ToArray();
        corrupted[corrupted.Length / 2] ^= 0xFF;
        _objects[key] = value with { Bytes = corrupted };
    }

    public void ChangeContentType(string key, string contentType) =>
        _objects[key] = _objects[key] with { ContentType = contentType };

    private sealed record StoredObject(byte[] Bytes, string ContentType);
}
