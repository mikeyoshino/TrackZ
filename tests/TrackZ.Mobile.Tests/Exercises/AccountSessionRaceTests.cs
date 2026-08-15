using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class AccountSessionRaceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-session-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Old_account_catalog_response_cannot_overwrite_new_account_or_repopulate_live_performance()
    {
        var oldId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var newId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var oldApi = new DelayedCatalogApi(Summary(oldId, "Old Press", 125m));
        var oldPicker = new ExercisePickerViewModel(
            cache, oldApi, new OnlineConnectivity(), new FixedClock(), boundary: boundary);

        await oldPicker.LoadAsync();
        var oldRefresh = oldPicker.RefreshCompletion;
        await oldApi.Entered;

        await boundary.ResetAsync(cache.ClearAllAsync);
        var newPicker = new ExercisePickerViewModel(
            cache, new ImmediateCatalogApi(Summary(newId, "New Press", 80m)),
            new OnlineConnectivity(), new FixedClock(), boundary: boundary);
        await newPicker.LoadAsync();
        await newPicker.RefreshCompletion;

        oldApi.Release();
        await oldRefresh;

        var persisted = Assert.Single(await cache.GetAllAsync());
        Assert.Equal(newId, persisted.Id);
        Assert.Equal("New Press", persisted.Name);
        Assert.Equal(80m, persisted.LastBestSet!.WeightKg);
        Assert.Empty(oldPicker.Exercises);
        Assert.Equal(newId, Assert.Single(newPicker.Exercises).Id);
    }

    [Fact]
    public async Task Session_reset_clears_outbox_library_thumbnails_and_live_collections_atomically()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var thumbnails = new RecordingThumbnailCache();
        var connectivity = new OfflineConnectivity();
        using var service = new CustomExerciseImageService(
            cache, connectivity, new NoopCustomApi(), new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), thumbnails, boundary);
        var custom = new CustomExerciseViewModel(
            service, cache, new ImmediateCatalogApi(Summary(Guid.NewGuid(), "Library", null, Guid.NewGuid())),
            new OnlineConnectivity(), thumbnails, boundary);
        custom.Name = "Old Draft";
        custom.BodyPart = BodyPart.Chest;
        custom.TrackingMode = TrackingMode.Weighted;
        custom.ExistingExerciseId = Guid.NewGuid();
        custom.ValidationErrors["name"] = ["Old error"];

        await service.SaveAsync(new CustomExerciseDraft(
            "Pending", BodyPart.Chest, TrackingMode.Weighted, null, null, null));
        await custom.LoadLibraryImagesAsync();
        Assert.NotEmpty(custom.LibraryImages);
        Assert.NotEmpty(await cache.GetPendingAsync());

        await boundary.ResetAsync(async cancellationToken =>
        {
            await service.ClearPrivateDataAsync(cancellationToken);
        });

        Assert.Empty(await cache.GetAllAsync());
        Assert.Empty(await cache.GetPendingAsync());
        Assert.Empty(await cache.GetLibraryImagesAsync());
        Assert.Empty(custom.LibraryImages);
        Assert.Equal(string.Empty, custom.Name);
        Assert.Null(custom.BodyPart);
        Assert.Null(custom.TrackingMode);
        Assert.Null(custom.ExistingExerciseId);
        Assert.Empty(custom.ValidationErrors);
        Assert.Equal(1, thumbnails.ClearCount);
    }

    [Fact]
    public async Task Stale_catalog_and_library_failures_cannot_repopulate_new_session_error_state()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var pickerApi = new DelayedFailingCatalogApi();
        var picker = new ExercisePickerViewModel(
            cache, pickerApi, new OnlineConnectivity(), new FixedClock(), boundary: boundary);
        await picker.LoadAsync();
        var pickerRefresh = picker.RefreshCompletion;
        await pickerApi.Entered;

        using var service = new CustomExerciseImageService(
            cache, new OfflineConnectivity(), new NoopCustomApi(), new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), new RecordingThumbnailCache(), boundary);
        var libraryApi = new DelayedFailingCatalogApi();
        var custom = new CustomExerciseViewModel(
            service, cache, libraryApi, new OnlineConnectivity(), new RecordingThumbnailCache(), boundary);
        var libraryRefresh = custom.LoadLibraryImagesAsync();
        await libraryApi.Entered;

        await boundary.ResetAsync(cache.ClearAllAsync);
        pickerApi.Release();
        libraryApi.Release();
        await Task.WhenAll(pickerRefresh, libraryRefresh);

        Assert.Null(picker.LastErrorCode);
        Assert.Null(custom.LastErrorCode);
    }

    [Fact]
    public async Task Old_account_library_response_is_discarded_after_new_account_metadata_commits()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var oldLibraryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var newLibraryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var delayed = new DelayedCatalogApi(Summary(Guid.NewGuid(), "Old Library", null, oldLibraryId));
        using var service = new CustomExerciseImageService(
            cache, new OfflineConnectivity(), new NoopCustomApi(), new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), new RecordingThumbnailCache(), boundary);
        var oldCustom = new CustomExerciseViewModel(
            service, cache, delayed, new OnlineConnectivity(), new RecordingThumbnailCache(), boundary);

        var oldLoad = oldCustom.LoadLibraryImagesAsync();
        await delayed.Entered;
        await boundary.ResetAsync(cache.ClearAllAsync);
        var generation = boundary.Capture();
        await boundary.TryCommitAsync(generation, token => cache.ReplaceLibraryImagesAsync([
            new CachedLibraryImage(newLibraryId, "New Library", "/local/new.jpg")
        ], token));

        delayed.Release();
        await oldLoad;

        var persisted = Assert.Single(await cache.GetLibraryImagesAsync());
        Assert.Equal(newLibraryId, persisted.ImageId);
        Assert.Empty(oldCustom.LibraryImages);
    }

    [Fact]
    public async Task Delayed_old_custom_create_cannot_restore_outbox_after_session_reset()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var delayed = new DelayedCustomApi();
        using var service = new CustomExerciseImageService(
            cache, new OnlineConnectivity(), delayed, new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), new RecordingThumbnailCache(), boundary);

        var oldSave = service.SaveAsync(new CustomExerciseDraft(
            "Old Pending", BodyPart.Chest, TrackingMode.Weighted, null, null, null));
        await delayed.Entered;
        await boundary.ResetAsync(service.ClearPrivateDataAsync);
        var generation = boundary.Capture();
        var newId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        await boundary.TryCommitAsync(generation, token => cache.ReplaceAllAsync([
            Summary(newId, "New Account", 70m)
        ], new FixedClock().UtcNow, token));

        delayed.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldSave);

        Assert.Empty(await cache.GetPendingAsync());
        Assert.Equal(newId, Assert.Single(await cache.GetAllAsync()).Id);
        Assert.Equal(0, delayed.UpdateCount);
    }

    [Fact]
    public async Task Stale_custom_sync_failure_cannot_publish_error_after_session_reset()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var connectivity = new MutableConnectivity(false);
        var delayed = new DelayedRejectingCustomApi();
        using var service = new CustomExerciseImageService(
            cache, connectivity, delayed, new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), new RecordingThumbnailCache(), boundary);
        await service.SaveAsync(new CustomExerciseDraft(
            "Old Pending", BodyPart.Chest, TrackingMode.Weighted, null, null, null));

        connectivity.IsOnline = true;
        var oldSync = service.SynchronizePendingAsync();
        await delayed.Entered;
        await boundary.ResetAsync(service.ClearPrivateDataAsync);
        delayed.Release();
        await oldSync;

        Assert.Null(service.LastSynchronizationError);
        Assert.Null(service.LastSynchronizationMessage);
        Assert.Empty(await cache.GetPendingAsync());
        Assert.Empty(await cache.GetFailedAsync());
    }

    [Fact]
    public async Task Save_queued_before_reset_cannot_persist_old_draft_in_new_session()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var api = new FirstCreateDelayedApi();
        using var service = new CustomExerciseImageService(
            cache, new OnlineConnectivity(), api, new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), new RecordingThumbnailCache(), boundary);
        var first = service.SaveAsync(new CustomExerciseDraft(
            "First Old Draft", BodyPart.Chest, TrackingMode.Weighted, null, null, null));
        await api.FirstEntered;
        var queuedOldSave = service.SaveAsync(new CustomExerciseDraft(
            "Queued Old Draft", BodyPart.Chest, TrackingMode.Weighted, null, null, null));

        await boundary.ResetAsync(service.ClearPrivateDataAsync);
        api.ReleaseFirst();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queuedOldSave);
        Assert.Empty(await cache.GetAllAsync());
        Assert.Equal(1, api.CreateCount);
    }

    [Fact]
    public async Task Delayed_refreshing_dispatch_cannot_turn_spinner_back_on_after_reset()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var dispatcher = new SecondCallGatedDispatcher();
        var picker = new ExercisePickerViewModel(
            cache, new ImmediateCatalogApi(Summary(Guid.NewGuid(), "Old", 50m)),
            new OnlineConnectivity(), new FixedClock(), dispatcher, boundary: boundary);

        await picker.LoadAsync();
        await dispatcher.SecondCallEntered;
        var reset = boundary.ResetAsync(cache.ClearAllAsync);
        dispatcher.ReleaseSecondCall();
        await Task.WhenAll(reset, picker.RefreshCompletion);

        Assert.False(picker.IsRefreshing);
        Assert.Empty(picker.Exercises);
    }

    [Fact]
    public async Task Custom_save_canceled_by_reset_is_discarded_without_stale_error()
    {
        var cache = new ExerciseCache(_databasePath);
        var boundary = new AccountSessionBoundary();
        var delayed = new DelayedCustomApi();
        using var service = new CustomExerciseImageService(
            cache, new OnlineConnectivity(), delayed, new NoopImageApi(), new MissingFileStore(),
            new FixedClock(), new RecordingThumbnailCache(), boundary);
        var custom = new CustomExerciseViewModel(service, cache, boundary: boundary)
        {
            Name = "Old Save",
            BodyPart = BodyPart.Chest,
            TrackingMode = TrackingMode.Weighted
        };

        var saving = custom.SaveAsync();
        await delayed.Entered;
        await boundary.ResetAsync(service.ClearPrivateDataAsync);
        delayed.Release();
        var saved = await saving;

        Assert.False(saved);
        Assert.Null(custom.LastErrorCode);
        Assert.Empty(custom.ValidationErrors);
        Assert.Empty(await cache.GetAllAsync());
    }

    private static ExerciseSummaryDto Summary(Guid id, string name, decimal? weight, Guid? libraryImageId = null) => new(
        id, name, BodyPart.Chest, TrackingMode.Weighted,
        libraryImageId is null ? null : $"/api/v1/media/exercise-images/{libraryImageId:D}/thumbnail",
        weight is null ? null : DateTimeOffset.Parse("2026-08-15T09:00:00Z"),
        weight is null ? null : new PerformanceSetDto(weight, null, 5),
        weight is null ? null : new PerformanceSetDto(weight, null, 5),
        false,
        libraryImageId);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    private sealed class DelayedCatalogApi(ExerciseSummaryDto result) : IExerciseCatalogApi
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        public async Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return [result];
        }
    }

    private sealed class ImmediateCatalogApi(ExerciseSummaryDto result) : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExerciseSummaryDto>>([result]);
    }

    private sealed class DelayedFailingCatalogApi : IExerciseCatalogApi
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        public async Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;
            throw new MobileApiException(BusinessErrorCode.InvalidRequest, "Old account failure.");
        }
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MutableConnectivity(bool online) : IConnectivityService
    {
        public bool IsOnline { get; set; } = online;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-15T10:00:00Z");
    }

    private sealed class SecondCallGatedDispatcher : IUiDispatcher
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public Task SecondCallEntered => _entered.Task;
        public void ReleaseSecondCall() => _release.TrySetResult();
        public async Task InvokeAsync(Action action)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                _entered.TrySetResult();
                await _release.Task;
            }
            action();
        }
    }

    private sealed class RecordingThumbnailCache : IExerciseThumbnailCache
    {
        public int ClearCount { get; private set; }
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(thumbnailUri is null ? null : "/local/library.jpg");
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopCustomApi : ICustomExerciseApi
    {
        public Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid());
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class DelayedCustomApi : ICustomExerciseApi
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public int UpdateCount { get; private set; }
        public void Release() => _release.TrySetResult();
        public async Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;
            return Guid.Parse("66666666-6666-6666-6666-666666666666");
        }
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class DelayedRejectingCustomApi : ICustomExerciseApi
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        public async Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task;
            throw new MobileApiException(BusinessErrorCode.ExerciseNameDuplicate, "Old account rejection.");
        }
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FirstCreateDelayedApi : ICustomExerciseApi
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;
        public Task FirstEntered => _entered.Task;
        public int CreateCount => Volatile.Read(ref _count);
        public void ReleaseFirst() => _release.TrySetResult();
        public async Task<Guid> CreateAsync(CustomExerciseDraft exercise, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _count);
            if (call == 1)
            {
                _entered.TrySetResult();
                await _release.Task;
            }
            return Guid.Parse($"{call:D8}-8888-8888-8888-888888888888");
        }
        public Task UpdateAsync(Guid exerciseId, CustomExerciseDraft exercise, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopImageApi : IExerciseImageApi
    {
        public Task<ImageUploadReservation> RequestUploadAsync(Guid exerciseId, string contentType, long length, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadContentAsync(Uri uploadUri, Stream original, string contentType, long length, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<UploadedExerciseImage> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MissingFileStore : IExerciseFileStore
    {
        public long GetLength(string path) => throw new NotSupportedException();
        public Stream OpenRead(string path) => throw new NotSupportedException();
    }
}
