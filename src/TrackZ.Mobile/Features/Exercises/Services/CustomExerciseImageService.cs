using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Identity;

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
    private readonly IAccountSessionBoundary _boundary;
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
        IExerciseThumbnailCache thumbnailCache,
        IAccountSessionBoundary? boundary = null)
    {
        _cache = cache;
        _connectivity = connectivity;
        _customApi = customApi;
        _imageApi = imageApi;
        _files = files;
        _clock = clock;
        _thumbnailCache = thumbnailCache;
        _boundary = boundary ?? new AccountSessionBoundary();
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    public Task PendingSynchronization => _pendingSynchronization;
    public BusinessErrorCode? LastSynchronizationError { get; private set; }
    public string? LastSynchronizationMessage { get; private set; }

    public async Task ClearPrivateDataAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _cache.ClearAllAsync(cancellationToken);
        await _thumbnailCache.ClearAsync(cancellationToken);
    }

    public Task<Guid> SaveAsync(CustomExerciseDraft draft, CancellationToken cancellationToken = default) =>
        SaveAsync(draft, _boundary.Capture(), cancellationToken);

    public async Task<Guid> SaveAsync(
        CustomExerciseDraft draft,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _synchronizationLock.WaitAsync(cancellationToken);
        try
        {
            PendingCustomExercise? pending = null;
            PendingCustomExercise? previousPending = null;
            CachedExercise? previousExercise = null;
            if (!await _boundary.TryCommitAsync(generation, async token =>
            {
                if (draft.ExistingExerciseId is { } existingId)
                {
                    previousPending = await _cache.FindPendingByLocalIdAsync(existingId, token);
                    previousExercise = await _cache.FindExerciseAsync(existingId, token);
                }
                pending = await PersistIntentAsync(draft, token);
            }, cancellationToken)) throw new OperationCanceledException("The account session changed.");
            var durable = pending ?? throw new InvalidOperationException("The pending intent was not persisted.");
            if (!_connectivity.IsOnline) return durable.LocalExerciseId;
            try
            {
                return await SynchronizeOneAsync(durable, generation, cancellationToken);
            }
            catch (Exception exception) when (IsDefinitiveLocalFailure(exception))
            {
                await MarkUserActionRequiredAsync(
                    durable.OperationId,
                    BusinessErrorCode.InternalServerError,
                    "The pending exercise requires attention.",
                    generation,
                    cancellationToken);
                throw new MobileApiException(
                    BusinessErrorCode.InternalServerError,
                    "The pending exercise requires attention.",
                    innerException: exception);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                return durable.ServerExerciseId ?? durable.LocalExerciseId;
            }
            catch (MobileApiException exception) when (!exception.IsRetryable)
            {
                await _boundary.TryCommitAsync(generation, async token =>
                {
                    var latest = await _cache.FindOutboxByOperationAsync(durable.OperationId, token);
                    if (HasAcknowledgedRemoteSideEffect(durable, latest))
                        await _cache.MarkPendingFailedAsync(
                            durable.OperationId, exception.ErrorCode, exception.Message, token);
                    else if (previousPending is not null)
                        await _cache.QueueAsync(previousPending, token);
                    else
                        await _cache.RollbackPendingAsync(
                            durable.OperationId, durable.LocalExerciseId, previousExercise, token);
                }, cancellationToken);
                throw;
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
            var generation = _boundary.Capture();
            IReadOnlyList<PendingCustomExercise> pendingRows = [];
            if (!await _boundary.TryCommitAsync(generation, async token =>
            {
                pendingRows = await _cache.GetPendingAsync(token);
            }, cancellationToken)) return;
            foreach (var pending in pendingRows)
            {
                try
                {
                    await SynchronizeOneAsync(pending, generation, cancellationToken);
                }
                catch (MobileApiException exception) when (!exception.IsRetryable)
                {
                    await MarkUserActionRequiredAsync(
                        pending.OperationId, exception.ErrorCode, exception.Message, generation, cancellationToken);
                }
                catch (Exception exception) when (IsDefinitiveLocalFailure(exception))
                {
                    await MarkUserActionRequiredAsync(
                        pending.OperationId,
                        BusinessErrorCode.InternalServerError,
                        "The pending exercise requires attention.",
                        generation,
                        cancellationToken);
                }
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
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _boundary.GetCancellationToken(generation));
        cancellationToken = linked.Token;
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
                current = current with
                {
                    ServerExerciseId = serverId,
                    OperationKind = PendingCustomOperationKind.Update
                };
                if (!await _boundary.TryCommitAsync(generation, token =>
                    _cache.QueueAsync(current, token), cancellationToken))
                    throw new OperationCanceledException("The account session changed.");
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
            if (!await _boundary.TryCommitAsync(generation, token => _cache.QueueAsync(current, token), cancellationToken))
                throw new OperationCanceledException("The account session changed.");
        }

        var saved = current.ServerExerciseId ?? throw new InvalidOperationException(
            "Pending details were marked saved without a server identity.");
        var currentDraft = ToDraft(current);
        if (current.LocalImagePath is null || current.LocalImageContentType is null)
        {
            if (!await _boundary.TryCommitAsync(generation, token => _cache.CompletePendingAsync(
                current.OperationId, current.LocalExerciseId,
                ToCached(saved, currentDraft, current.LocalPreviewPath), token), cancellationToken))
                throw new OperationCanceledException("The account session changed.");
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
            if (!await _boundary.TryCommitAsync(generation, token => _cache.QueueAsync(current, token), cancellationToken))
                throw new OperationCanceledException("The account session changed.");
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
            if (!await _boundary.TryCommitAsync(generation, token => _cache.QueueAsync(current, token), cancellationToken))
                throw new OperationCanceledException("The account session changed.");
        }

        var uploadId = current.UploadId ?? throw new InvalidOperationException(
            "An uploaded image must retain its reservation identity.");
        var uploaded = await _imageApi.CompleteUploadAsync(uploadId, cancellationToken);
        var localizedThumbnail = await CacheUploadedThumbnailAsync(uploaded, cancellationToken);
        if (!await _boundary.TryCommitAsync(generation, token =>
        {
            return _cache.CompletePendingAsync(
                current.OperationId, current.LocalExerciseId,
                ToCached(saved, currentDraft, localizedThumbnail), token);
        }, cancellationToken)) throw new OperationCanceledException("The account session changed.");
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

    private async Task MarkUserActionRequiredAsync(
        Guid operationId,
        BusinessErrorCode code,
        string message,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        await _boundary.TryCommitAsync(generation, async token =>
        {
            await _cache.MarkPendingFailedAsync(operationId, code, message, token);
            LastSynchronizationError = code;
            LastSynchronizationMessage = message;
        }, cancellationToken);
    }

    private static bool HasAcknowledgedRemoteSideEffect(
        PendingCustomExercise original,
        PendingCustomExercise? latest) =>
        latest is not null
        && ((original.ServerExerciseId is null && latest.ServerExerciseId is not null)
            || latest.Phase is not PendingCustomSyncPhase.PendingDetails);

    private static bool IsDefinitiveLocalFailure(Exception exception) => exception is
        FileNotFoundException
        or DirectoryNotFoundException
        or UnauthorizedAccessException
        or InvalidDataException
        or UriFormatException
        or ArgumentException
        or InvalidOperationException;

    private void OnConnectivityChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed || !_connectivity.IsOnline) return;
        _pendingSynchronization = SynchronizeAfterConnectivityAsync();
    }

    private async Task SynchronizeAfterConnectivityAsync()
    {
        var generation = _boundary.Capture();
        try
        {
            await SynchronizePendingAsync();
            await _boundary.TryCommitAsync(generation, _ =>
            {
                LastSynchronizationError = null;
                LastSynchronizationMessage = null;
                return Task.CompletedTask;
            });
        }
        catch (MobileApiException exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                LastSynchronizationError = exception.ErrorCode;
                LastSynchronizationMessage = exception.Message;
                return Task.CompletedTask;
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                LastSynchronizationError = BusinessErrorCode.InternalServerError;
                LastSynchronizationMessage = exception.Message;
                return Task.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            // Account reset invalidates the prior generation and owns durable cleanup.
        }
    }
}
