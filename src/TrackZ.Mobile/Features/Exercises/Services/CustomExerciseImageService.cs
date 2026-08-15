using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class CustomExerciseImageService : IDisposable
{
    private readonly ExerciseCache _cache;
    private readonly IConnectivityService _connectivity;
    private readonly ICustomExerciseApi _customApi;
    private readonly IExerciseImageApi _imageApi;
    private readonly IExerciseFileStore _files;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _synchronizationLock = new(1, 1);
    private Task _pendingSynchronization = Task.CompletedTask;
    private bool _disposed;

    public CustomExerciseImageService(
        ExerciseCache cache,
        IConnectivityService connectivity,
        ICustomExerciseApi customApi,
        IExerciseImageApi imageApi,
        IExerciseFileStore files,
        IClock clock)
    {
        _cache = cache;
        _connectivity = connectivity;
        _customApi = customApi;
        _imageApi = imageApi;
        _files = files;
        _clock = clock;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public Task PendingSynchronization => _pendingSynchronization;
    public BusinessErrorCode? LastSynchronizationError { get; private set; }

    public async Task<Guid> SaveAsync(CustomExerciseDraft draft, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connectivity.IsOnline)
        {
            return await QueueAsync(draft, draft.ExistingExerciseId, cancellationToken);
        }

        try
        {
            return await SaveOnlineAsync(draft, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return await QueueAsync(draft, draft.ExistingExerciseId, cancellationToken);
        }
    }

    public async Task SynchronizePendingAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connectivity.IsOnline) return;
        await _synchronizationLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var pending in await _cache.GetPendingAsync(cancellationToken))
            {
                var draft = new CustomExerciseDraft(
                    pending.Name,
                    pending.BodyPart,
                    pending.TrackingMode,
                    pending.LibraryImageId,
                    pending.LocalImagePath,
                    pending.LocalImageContentType,
                    pending.ServerExerciseId);
                var saved = await SaveOnlineDetailsAsync(draft, cancellationToken);
                if (pending.ServerExerciseId is null)
                    await _cache.SetPendingServerIdAsync(pending.OperationId, saved, cancellationToken);
                var thumbnail = draft.LocalImagePath;
                if (draft.LocalImagePath is not null && draft.LocalImageContentType is not null)
                {
                    thumbnail = (await UploadOriginalAsync(saved, draft.LocalImagePath, draft.LocalImageContentType, cancellationToken)).ThumbnailUrl;
                }
                await _cache.CompletePendingAsync(
                    pending.OperationId,
                    pending.LocalExerciseId,
                    ToCached(saved, draft, thumbnail),
                    cancellationToken);
            }
        }
        finally
        {
            _synchronizationLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _synchronizationLock.Dispose();
    }

    private async Task<Guid> SaveOnlineAsync(CustomExerciseDraft draft, CancellationToken cancellationToken)
    {
        var saved = await SaveOnlineDetailsAsync(draft, cancellationToken);
        string? thumbnail = null;
        if (draft.LocalImagePath is not null && draft.LocalImageContentType is not null)
        {
            try
            {
                thumbnail = (await UploadOriginalAsync(saved, draft.LocalImagePath, draft.LocalImageContentType, cancellationToken)).ThumbnailUrl;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                await _cache.QueueAsync(new PendingCustomExercise(
                    Guid.NewGuid(),
                    saved,
                    saved,
                    draft.Name,
                    draft.BodyPart,
                    draft.TrackingMode,
                    draft.LibraryImageId,
                    draft.LocalImagePath,
                    draft.LocalImageContentType,
                    _clock.UtcNow,
                    draft.LocalPreviewPath), cancellationToken);
                return saved;
            }
        }
        await _cache.UpsertServerExerciseAsync(ToCached(saved, draft, thumbnail), cancellationToken);
        return saved;
    }

    private async Task<Guid> SaveOnlineDetailsAsync(CustomExerciseDraft draft, CancellationToken cancellationToken)
    {
        if (draft.ExistingExerciseId is { } existing)
        {
            await _customApi.UpdateAsync(existing, draft, cancellationToken);
            return existing;
        }
        return await _customApi.CreateAsync(draft, cancellationToken);
    }

    private async Task<Guid> QueueAsync(
        CustomExerciseDraft draft,
        Guid? serverExerciseId,
        CancellationToken cancellationToken)
    {
        var localId = serverExerciseId ?? Guid.NewGuid();
        await _cache.QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(),
            localId,
            serverExerciseId,
            draft.Name,
            draft.BodyPart,
            draft.TrackingMode,
            draft.LibraryImageId,
            draft.LocalImagePath,
            draft.LocalImageContentType,
            _clock.UtcNow,
            draft.LocalPreviewPath), cancellationToken);
        return localId;
    }

    private async Task<UploadedExerciseImage> UploadOriginalAsync(
        Guid exerciseId,
        string path,
        string contentType,
        CancellationToken cancellationToken)
    {
        var length = _files.GetLength(path);
        var reservation = await _imageApi.RequestUploadAsync(exerciseId, contentType, length, cancellationToken);
        await using (var original = _files.OpenRead(path))
        {
            await _imageApi.UploadContentAsync(reservation.UploadUri, original, contentType, length, cancellationToken);
        }
        return await _imageApi.CompleteUploadAsync(reservation.UploadId, cancellationToken);
    }

    private CachedExercise ToCached(Guid id, CustomExerciseDraft draft, string? thumbnail) => new()
    {
        Id = id,
        Name = draft.Name,
        BodyPart = draft.BodyPart,
        TrackingMode = draft.TrackingMode,
        ThumbnailUri = thumbnail ?? draft.LocalPreviewPath,
        IsCustom = true,
        LastSyncedAt = _clock.UtcNow
    };

    private void OnConnectivityChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed || !_connectivity.IsOnline) return;
        _pendingSynchronization = SynchronizeAfterConnectivityAsync();
    }

    private async Task SynchronizeAfterConnectivityAsync()
    {
        try
        {
            await SynchronizePendingAsync();
            LastSynchronizationError = null;
        }
        catch (MobileApiException exception)
        {
            LastSynchronizationError = exception.ErrorCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            LastSynchronizationError = BusinessErrorCode.InternalServerError;
        }
    }
}
