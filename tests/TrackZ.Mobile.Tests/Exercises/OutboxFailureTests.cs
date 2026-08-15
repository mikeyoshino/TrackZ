using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class OutboxFailureTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-outbox-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Online_business_rejection_rolls_back_persisted_intent_and_surfaces_stable_error()
    {
        var cache = new ExerciseCache(_databasePath);
        using var service = Service(cache, new Connectivity(true), new RejectPoisonApi());

        var error = await Assert.ThrowsAsync<MobileApiException>(() => service.SaveAsync(Draft("Poison")));

        Assert.Equal(BusinessErrorCode.ExerciseNameDuplicate, error.ErrorCode);
        Assert.Equal("An exercise with this name already exists.", error.Message);
        Assert.Empty(await cache.GetPendingAsync());
        Assert.Empty(await cache.GetAllAsync());
    }

    [Fact]
    public async Task Reconnect_marks_poison_for_user_action_and_continues_with_later_valid_row()
    {
        var cache = new ExerciseCache(_databasePath);
        var connectivity = new Connectivity(false);
        var api = new RejectPoisonApi();
        using var service = Service(cache, connectivity, api);
        await service.SaveAsync(Draft("Poison"));
        await service.SaveAsync(Draft("Valid"));

        connectivity.IsOnline = true;
        await service.SynchronizePendingAsync();

        Assert.Empty(await cache.GetPendingAsync());
        var failed = Assert.Single(await cache.GetFailedAsync());
        Assert.Equal("Poison", failed.Name);
        Assert.Equal(BusinessErrorCode.ExerciseNameDuplicate, failed.FailureCode);
        Assert.Equal("An exercise with this name already exists.", failed.FailureMessage);
        Assert.Contains(await cache.GetAllAsync(), exercise =>
            exercise.Name == "Valid" && !exercise.IsPendingSync);
        Assert.Equal(2, api.CreateNames.Count);
    }

    [Fact]
    public async Task Editing_failed_create_reactivates_the_same_local_operation_for_user_retry()
    {
        var cache = new ExerciseCache(_databasePath);
        var connectivity = new Connectivity(false);
        using var service = Service(cache, connectivity, new RejectPoisonApi());
        var localId = await service.SaveAsync(Draft("Poison"));
        connectivity.IsOnline = true;
        await service.SynchronizePendingAsync();
        var failed = Assert.Single(await cache.GetFailedAsync());

        connectivity.IsOnline = false;
        await service.SaveAsync(Draft("Fixed") with { ExistingExerciseId = localId });

        var pending = Assert.Single(await cache.GetPendingAsync());
        Assert.Equal(failed.OperationId, pending.OperationId);
        Assert.Equal(PendingCustomOperationKind.Create, pending.OperationKind);
        Assert.Empty(await cache.GetFailedAsync());
    }

    [Fact]
    public async Task Online_rejection_after_server_create_preserves_acknowledged_phase_for_user_action()
    {
        var cache = new ExerciseCache(_databasePath);
        using var service = Service(
            cache, new Connectivity(true), new SuccessCustomApi(),
            new RejectReservationApi(), new LengthOnlyFiles());

        var error = await Assert.ThrowsAsync<MobileApiException>(() => service.SaveAsync(
            Draft("Remote Created") with { LocalImagePath = "original.jpg", LocalImageContentType = "image/jpeg" }));

        Assert.Equal(BusinessErrorCode.InvalidRequest, error.ErrorCode);
        Assert.Empty(await cache.GetPendingAsync());
        var failed = Assert.Single(await cache.GetFailedAsync());
        Assert.NotNull(failed.ServerExerciseId);
        Assert.Equal(PendingCustomSyncPhase.DetailsSaved, failed.Phase);
        Assert.Contains(await cache.GetAllAsync(), exercise => exercise.Name == "Remote Created" && exercise.IsPendingSync);
    }

    [Fact]
    public async Task Deterministic_missing_original_is_failed_without_blocking_later_valid_row()
    {
        var cache = new ExerciseCache(_databasePath);
        var connectivity = new Connectivity(false);
        using var service = Service(
            cache, connectivity, new SuccessCustomApi(), new NoopImageApi(), new SelectiveFiles());
        await service.SaveAsync(Draft("Missing Original") with
        {
            LocalImagePath = "missing.jpg",
            LocalImageContentType = "image/jpeg"
        });
        await service.SaveAsync(Draft("Later Valid"));

        connectivity.IsOnline = true;
        await service.SynchronizePendingAsync();

        Assert.Empty(await cache.GetPendingAsync());
        var failed = Assert.Single(await cache.GetFailedAsync());
        Assert.Equal("Missing Original", failed.Name);
        Assert.Equal(BusinessErrorCode.InternalServerError, failed.FailureCode);
        Assert.Equal("The pending exercise requires attention.", failed.FailureMessage);
        Assert.Contains(await cache.GetAllAsync(), exercise => exercise.Name == "Later Valid" && !exercise.IsPendingSync);
    }

    private static CustomExerciseDraft Draft(string name) =>
        new(name, BodyPart.Chest, TrackingMode.Weighted, null, null, null);

    private static CustomExerciseImageService Service(
        ExerciseCache cache,
        Connectivity connectivity,
        ICustomExerciseApi api,
        IExerciseImageApi? imageApi = null,
        IExerciseFileStore? files = null) => new(
            cache, connectivity, api, imageApi ?? new NoopImageApi(), files ?? new MissingFiles(), new Clock(),
            new NoopThumbnailCache(), new AccountSessionBoundary());

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    private sealed class RejectPoisonApi : ICustomExerciseApi
    {
        public List<string> CreateNames { get; } = [];
        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            CreateNames.Add(exercise.Name);
            if (exercise.Name == "Poison")
                throw new MobileApiException(
                    BusinessErrorCode.ExerciseNameDuplicate,
                    "An exercise with this name already exists.");
            return Task.FromResult(Guid.Parse("99999999-9999-9999-9999-999999999999"));
        }
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SuccessCustomApi : ICustomExerciseApi
    {
        private int _next;
        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            var suffix = Interlocked.Increment(ref _next);
            return Task.FromResult(Guid.Parse($"{suffix:D8}-9999-9999-9999-999999999999"));
        }
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RejectReservationApi : IExerciseImageApi
    {
        public Task<ImageUploadReservation> RequestUploadAsync(Guid exerciseId, string contentType, long length, CancellationToken cancellationToken = default) =>
            throw new MobileApiException(BusinessErrorCode.InvalidRequest, "The image upload was rejected.");
        public Task UploadContentAsync(Uri uploadUri, Stream original, string contentType, long length, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Connectivity(bool online) : IConnectivityService
    {
        public bool IsOnline { get; set; } = online;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-15T10:00:00Z"); }
    private sealed class NoopThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
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
    private sealed class LengthOnlyFiles : IExerciseFileStore
    {
        public long GetLength(string path) => 3;
        public Stream OpenRead(string path) => new MemoryStream([1, 2, 3]);
    }
    private sealed class SelectiveFiles : IExerciseFileStore
    {
        public long GetLength(string path) => throw new FileNotFoundException("The original image is missing.", path);
        public Stream OpenRead(string path) => throw new FileNotFoundException("The original image is missing.", path);
    }
}
