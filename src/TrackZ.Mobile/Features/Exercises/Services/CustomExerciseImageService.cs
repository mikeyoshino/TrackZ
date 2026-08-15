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
    private readonly IExerciseThumbnailCache _thumbnailCache;
    private readonly SemaphoreSlim _synchronizationLock = new(1, 1);
    private Task _pendingSynchronization = Task.CompletedTask;
    private bool _disposed;

    public CustomExerciseImageService(
        ExerciseCache cache,
        IConnectivityService connectivity,
        ICustomExerciseApi customApi,
        IExerciseImageApi imageApi,
        IExerciseFileStore files,
        IClock clock,
        IExerciseThumbnailCache thumbnailCache)
    {
        _cache = cache;
        _connectivity = connectivity;
        _customApi = customApi;
        _imageApi = imageApi;
        _files = files;
        _clock = clock;
        _thumbnailCache = thumbnailCache;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public Task PendingSynchronization => _pendingSynchronization;
    public BusinessErrorCode? LastSynchronizationError { get; private set; }

    public async Task ClearPrivateDataAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _synchronizationLock.WaitAsync(cancellationToken);
        try
        {
            await _cache.ClearAllAsync(cancellationToken);
            await _thumbnailCache.ClearAsync(cancellationToken);
        }
        finally
        {
            _synchronizationLock.Release();
        }
    }

    public async Task<Guid> SaveAsync(CustomExerciseDraft draft, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _synchronizationLock.WaitAsync(cancellationToken);
        try
        {
            var pending = await PersistIntentAsync(draft, cancellationToken);
            if (!_connectivity.IsOnline) return pending.LocalExerciseId;
            try
            {
                return await SynchronizeOneAsync(pending, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                return pending.ServerExerciseId ?? pending.LocalExerciseId;
            }
        }
        finally
        {
            _synchronizationLock.Release();
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
                await SynchronizeOneAsync(pending, cancellationToken);
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

    private async Task<PendingCustomExercise> PersistIntentAsync(
        CustomExerciseDraft draft,
        CancellationToken cancellationToken)
    {
        var existingPending = draft.ExistingExerciseId is { } localId
            ? await _cache.FindPendingByLocalIdAsync(localId, cancellationToken)
            : null;
        var choosingLibraryImage = draft.LibraryImageId is not null;
        var localImagePath = choosingLibraryImage ? null : draft.LocalImagePath ?? existingPending?.LocalImagePath;
        var localImageContentType = choosingLibraryImage ? null : draft.LocalImageContentType ?? existingPending?.LocalImageContentType;
        var imageChanged = choosingLibraryImage
            || (draft.LocalImagePath is not null
                && !string.Equals(draft.LocalImagePath, existingPending?.LocalImagePath, StringComparison.Ordinal));
        var resumePhase = (imageChanged ? null : existingPending?.Phase) switch
        {
            PendingCustomSyncPhase.UploadReserved => PendingCustomSyncPhase.PendingDetailsUploadReserved,
            PendingCustomSyncPhase.ContentUploaded => PendingCustomSyncPhase.PendingDetailsContentUploaded,
            PendingCustomSyncPhase.PendingDetailsUploadReserved => PendingCustomSyncPhase.PendingDetailsUploadReserved,
            PendingCustomSyncPhase.PendingDetailsContentUploaded => PendingCustomSyncPhase.PendingDetailsContentUploaded,
            _ => PendingCustomSyncPhase.PendingDetails
        };
        var pending = new PendingCustomExercise(
            existingPending?.OperationId ?? Guid.NewGuid(),
            existingPending?.LocalExerciseId ?? draft.ExistingExerciseId ?? Guid.NewGuid(),
            existingPending is null ? draft.ExistingExerciseId : existingPending.ServerExerciseId,
            draft.Name,
            draft.BodyPart,
            draft.TrackingMode,
            draft.LibraryImageId,
            localImagePath,
            localImageContentType,
            existingPending?.CreatedAt ?? _clock.UtcNow,
            draft.LocalPreviewPath ?? existingPending?.LocalPreviewPath,
            existingPending?.ServerExerciseId is not null
                ? PendingCustomOperationKind.Update
                : existingPending?.OperationKind ?? (draft.ExistingExerciseId is null
                    ? PendingCustomOperationKind.Create
                    : PendingCustomOperationKind.Update),
            resumePhase,
            imageChanged ? null : existingPending?.UploadId,
            imageChanged ? null : existingPending?.UploadUri);
        await _cache.QueueAsync(pending, cancellationToken);
        return pending;
    }

    private async Task<Guid> SynchronizeOneAsync(
        PendingCustomExercise pending,
        CancellationToken cancellationToken)
    {
        var current = pending;
        if (current.Phase is PendingCustomSyncPhase.PendingDetails
            or PendingCustomSyncPhase.PendingDetailsUploadReserved
            or PendingCustomSyncPhase.PendingDetailsContentUploaded)
        {
            var pendingDetailsPhase = current.Phase;
            var draft = ToDraft(current);
            Guid serverId;
            if (current.OperationKind == PendingCustomOperationKind.Create)
            {
                serverId = await _customApi.CreateAsync(draft, cancellationToken);
                await _customApi.UpdateAsync(serverId, draft, cancellationToken);
            }
            else
            {
                serverId = current.ServerExerciseId ?? throw new InvalidOperationException(
                    "A pending update must have a server exercise identity.");
                await _customApi.UpdateAsync(serverId, draft, cancellationToken);
            }
            current = current with
            {
                ServerExerciseId = serverId,
                Phase = pendingDetailsPhase switch
                {
                    PendingCustomSyncPhase.PendingDetailsUploadReserved => PendingCustomSyncPhase.UploadReserved,
                    PendingCustomSyncPhase.PendingDetailsContentUploaded => PendingCustomSyncPhase.ContentUploaded,
                    _ => PendingCustomSyncPhase.DetailsSaved
                }
            };
            await _cache.QueueAsync(current, cancellationToken);
        }

        var saved = current.ServerExerciseId ?? throw new InvalidOperationException(
            "Pending details were marked saved without a server identity.");
        var currentDraft = ToDraft(current);
        if (current.LocalImagePath is null || current.LocalImageContentType is null)
        {
            await _cache.CompletePendingAsync(
                current.OperationId,
                current.LocalExerciseId,
                ToCached(saved, currentDraft, current.LocalPreviewPath),
                cancellationToken);
            return saved;
        }

        if (current.Phase == PendingCustomSyncPhase.DetailsSaved)
        {
            var reservation = await _imageApi.RequestUploadAsync(
                saved,
                current.LocalImageContentType,
                _files.GetLength(current.LocalImagePath),
                cancellationToken);
            current = current with
            {
                Phase = PendingCustomSyncPhase.UploadReserved,
                UploadId = reservation.UploadId,
                UploadUri = reservation.UploadUri.OriginalString
            };
            await _cache.QueueAsync(current, cancellationToken);
        }

        if (current.Phase == PendingCustomSyncPhase.UploadReserved)
        {
            var uploadUri = current.UploadUri is null
                ? throw new InvalidOperationException("A reserved upload must have a content URI.")
                : new Uri(current.UploadUri, UriKind.RelativeOrAbsolute);
            await using (var original = _files.OpenRead(current.LocalImagePath))
            {
                await _imageApi.UploadContentAsync(
                    uploadUri,
                    original,
                    current.LocalImageContentType,
                    _files.GetLength(current.LocalImagePath),
                    cancellationToken);
            }
            current = current with { Phase = PendingCustomSyncPhase.ContentUploaded };
            await _cache.QueueAsync(current, cancellationToken);
        }

        var uploadId = current.UploadId ?? throw new InvalidOperationException(
            "An uploaded image must retain its reservation identity.");
        var uploaded = await _imageApi.CompleteUploadAsync(uploadId, cancellationToken);
        var thumbnail = await CacheUploadedThumbnailAsync(uploaded, cancellationToken);
        await _cache.CompletePendingAsync(
            current.OperationId,
            current.LocalExerciseId,
            ToCached(saved, currentDraft, thumbnail),
            cancellationToken);
        return saved;
    }

    private static CustomExerciseDraft ToDraft(PendingCustomExercise pending) => new(
        pending.Name,
        pending.BodyPart,
        pending.TrackingMode,
        pending.LibraryImageId,
        pending.LocalImagePath,
        pending.LocalImageContentType,
        pending.ServerExerciseId,
        pending.LocalPreviewPath,
        pending.OperationId);

    private CachedExercise ToCached(Guid id, CustomExerciseDraft draft, string? thumbnail) => new()
    {
        Id = id,
        Name = draft.Name,
        BodyPart = draft.BodyPart,
        TrackingMode = draft.TrackingMode,
        ThumbnailUri = thumbnail ?? draft.LocalPreviewPath,
        LibraryImageId = draft.LibraryImageId,
        IsCustom = true,
        LastSyncedAt = _clock.UtcNow
    };

    private Task<string?> CacheUploadedThumbnailAsync(
        UploadedExerciseImage image,
        CancellationToken cancellationToken) =>
        _thumbnailCache.CacheAsync(image.ThumbnailUrl, cancellationToken);

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
