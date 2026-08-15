using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class LibraryMetadataRefreshTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-library-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Metadata_is_usable_before_bounded_failure_isolated_thumbnail_fills_finish()
    {
        var cache = new ExerciseCache(_databasePath);
        var thumbnails = new GatedThumbnailCache();
        var published = Enumerable.Range(1, 6).Select(index => Summary(index)).ToArray();
        var service = new CustomExerciseImageService(
            cache, new Connectivity(true), new NoopCustomApi(), new NoopImageApi(), new MissingFiles(),
            new Clock(), thumbnails, new AccountSessionBoundary());
        var boundary = new AccountSessionBoundary();
        var sut = new CustomExerciseViewModel(
            service, cache, new CatalogApi(published), new Connectivity(true), thumbnails, boundary);

        var load = sut.LoadLibraryImagesAsync();
        await thumbnails.FirstFillEntered;
        try
        {
            Assert.Equal(6, sut.LibraryImages.Count);
            Assert.Equal(6, (await cache.GetLibraryImagesAsync()).Count);
            Assert.InRange(thumbnails.MaximumConcurrency, 1, 4);
        }
        finally
        {
            thumbnails.Release();
        }

        await load;

        Assert.Equal(6, sut.LibraryImages.Count);
        Assert.Equal(4, thumbnails.MaximumConcurrency);
        Assert.Null(sut.LibraryImages.Single(image => image.Name == "Library 3").ThumbnailUri);
        Assert.All(sut.LibraryImages.Where(image => image.Name != "Library 3"), image =>
            Assert.StartsWith("/local/", image.ThumbnailUri, StringComparison.Ordinal));
    }

    private static ExerciseSummaryDto Summary(int index)
    {
        var exerciseId = Guid.Parse($"{index:D8}-1111-2222-3333-444444444444");
        var imageId = Guid.Parse($"{index:D8}-aaaa-bbbb-cccc-dddddddddddd");
        return new ExerciseSummaryDto(
            exerciseId, $"Library {index}", BodyPart.Chest, TrackingMode.Weighted,
            $"/api/v1/media/exercise-images/{imageId:D}/thumbnail", null, null, null, false, imageId);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    private sealed class GatedThumbnailCache : IExerciseThumbnailCache
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximum;
        public Task FirstFillEntered => _entered.Task;
        public int MaximumConcurrency => Volatile.Read(ref _maximum);
        public void Release() => _release.TrySetResult();

        public async Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            do
            {
                observed = Volatile.Read(ref _maximum);
            } while (active > observed && Interlocked.CompareExchange(ref _maximum, active, observed) != observed);
            _entered.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                if (thumbnailUri!.Contains("00000003", StringComparison.Ordinal))
                    throw new IOException("One preview failed.");
                return $"/local/{thumbnailUri[(thumbnailUri.LastIndexOf('/') + 1)..]}.jpg";
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class CatalogApi(IReadOnlyList<ExerciseSummaryDto> exercises) : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(exercises);
    }

    private sealed class Connectivity(bool online) : IConnectivityService
    {
        public bool IsOnline => online;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class NoopCustomApi : ICustomExerciseApi
    {
        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class NoopImageApi : IExerciseImageApi
    {
        public Task<ImageUploadReservation> RequestUploadAsync(Guid exerciseId, string contentType, long length, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadContentAsync(Uri uploadUri, Stream original, string contentType, long length, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class MissingFiles : IExerciseFileStore
    {
        public long GetLength(string path) => throw new NotSupportedException();
        public Stream OpenRead(string path) => throw new NotSupportedException();
    }
}
