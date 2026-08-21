using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Exercises;
using TrackZ.Infrastructure.Persistence.Seed;
using TrackZ.Infrastructure.Tests.Persistence;

namespace TrackZ.Infrastructure.Tests.Seed;

public sealed class ExerciseCatalogDeploymentTests
{
    [Fact]
    public async Task Partial_storage_failure_leaves_database_empty_and_retry_deploys_all_draft_assets()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var storage = new FailureInjectingStorage(failOnPut: 17);
        var deployment = new ObjectStorageExerciseCatalogAssetDeployment(storage);
        var command = new ExerciseCatalogDeploymentService(
            deployment,
            new ExerciseCatalogSeeder(database.Db, deployment));

        await Assert.ThrowsAsync<IOException>(() => command.DeployAndSeedAsync(CatalogPath));

        Assert.Empty(await database.Db.Exercises.ToArrayAsync());
        Assert.Empty(await database.Db.ExerciseImages.ToArrayAsync());
        Assert.Equal(16, storage.Keys.Count);

        storage.FailOnPut = null;
        await command.DeployAndSeedAsync(CatalogPath);

        Assert.Equal(96, storage.Keys.Count);
        Assert.Equal(48, await database.Db.Exercises.CountAsync());
        var images = await database.Db.ExerciseImages.ToArrayAsync();
        Assert.Equal(48, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState);
            Assert.Null(image.ReviewedByUserId);
            Assert.Null(image.ReviewedAt);
            Assert.Null(image.PublishedAt);
            Assert.False(image.AnatomyApproved);
            Assert.False(image.MovementApproved);
            Assert.False(image.RightsApproved);
        });

        await command.DeployAndSeedAsync(CatalogPath);

        var redeployedImages = await database.Db.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(48, redeployedImages.Length);
        Assert.All(redeployedImages, image => Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState));
    }

    [Fact]
    public async Task Conflicting_deployed_object_fails_before_any_upload_or_database_seed()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var storage = new FailureInjectingStorage();
        var first = ExerciseManifest.Load(CatalogPath)[0];
        storage.Seed(ExerciseCatalogSeeder.MasterKey(first.Id), [0xBA, 0xD0], "image/png");
        var deployment = new ObjectStorageExerciseCatalogAssetDeployment(storage);
        var command = new ExerciseCatalogDeploymentService(
            deployment,
            new ExerciseCatalogSeeder(database.Db, deployment));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.DeployAndSeedAsync(CatalogPath));

        Assert.Contains("conflict", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, storage.PutCount);
        Assert.Empty(await database.Db.Exercises.ToArrayAsync());
        Assert.Empty(await database.Db.ExerciseImages.ToArrayAsync());
    }

    private static string CatalogPath => Path.Combine(RepositoryRoot, "assets", "exercises", "catalog.json");

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

    private sealed class FailureInjectingStorage(int? failOnPut = null) : IObjectStorage
    {
        private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _objects = new(StringComparer.Ordinal);
        public int? FailOnPut { get; set; } = failOnPut;
        public int PutCount { get; private set; }
        public IReadOnlyCollection<string> Keys => _objects.Keys;

        public void Seed(string key, byte[] bytes, string contentType) => _objects[key] = (bytes, contentType);

        public Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken) =>
            Task.FromResult(_objects.TryGetValue(key, out var value)
                ? new ObjectStorageObject(value.Bytes.Length, value.ContentType, new MemoryStream(value.Bytes, writable: false))
                : null);

        public async Task PutAsync(string ownerPrefix, string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            PutCount++;
            if (PutCount == FailOnPut) throw new IOException("Injected object-storage outage.");
            using var bytes = new MemoryStream();
            await content.CopyToAsync(bytes, cancellationToken);
            _objects[key] = (bytes.ToArray(), contentType);
        }

        public Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The deployment command never deletes catalog objects.");
    }
}
