using MediatR;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Media.CompleteUpload;
public sealed record CompleteImageUploadCommand(Guid UploadId) : IRequest<ExerciseImageDto>;
public sealed record ExerciseImageDto(Guid Id, string MasterUrl, string ThumbnailUrl);

public sealed class CompleteImageUploadHandler(IExerciseImageUploadStore store, IObjectStorage storage, IImageProcessor processor, ICurrentUser currentUser) : IRequestHandler<CompleteImageUploadCommand, ExerciseImageDto>
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public async Task<ExerciseImageDto> Handle(CompleteImageUploadCommand request, CancellationToken cancellationToken)
    {
        var owner = currentUser.UserId;
        var ticket = await store.FindOwnedTicketAsync(request.UploadId, owner, cancellationToken) ?? throw Missing();
        if (ticket.State == ImageUploadState.Completed && ticket.ExerciseImageId is { } completeId) return Dto(completeId);
        var now = DateTimeOffset.UtcNow;
        if (ticket.IsExpired(now)) throw Missing();
        if (ticket.State is not (ImageUploadState.Uploaded or ImageUploadState.Processing)) throw Missing();
        if (!ticket.TryClaim(now, Lease)) throw Processing();
        await store.SaveAsync(cancellationToken);

        var processingLeaseId = ticket.ProcessingLeaseId ?? throw Processing();
        string? masterKey = null; string? thumbnailKey = null;
        try
        {
            var staged = await storage.GetAsync($"staging/{owner:D}/", ticket.StagingObjectKey, cancellationToken) ?? throw InvalidImage();
            await using var objectStream = staged.Content;
            var bytes = await ReadBoundedAsync(objectStream, ticket.DeclaredLength, cancellationToken);
            await using var bounded = new MemoryStream(bytes, writable: false);
            var rendered = await processor.ProcessExerciseImageAsync(bounded, cancellationToken);
            if (!string.Equals(ticket.DeclaredContentType, rendered.DetectedContentType, StringComparison.Ordinal)) throw InvalidImage();
            var root = $"private/{owner:D}/{ticket.ExerciseDefinitionId:D}/{ticket.Id:D}/{processingLeaseId:D}";
            masterKey = root + "/master.jpg"; thumbnailKey = root + "/thumbnail.jpg";
            await using var master = new MemoryStream(rendered.Master, writable: false);
            await storage.PutAsync($"private/{owner:D}/", masterKey, master, rendered.ContentType, cancellationToken);
            await using var thumbnail = new MemoryStream(rendered.Thumbnail, writable: false);
            await storage.PutAsync($"private/{owner:D}/", thumbnailKey, thumbnail, rendered.ContentType, cancellationToken);
            ExerciseImage image;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Once the durable phase begins it cannot be cancelled: an ambiguous acknowledgement
                // is reconciled against the database instead of compensating potentially committed keys.
                image = await store.CommitCompletionAsync(ticket.Id, owner, processingLeaseId, masterKey, thumbnailKey, CancellationToken.None);
            }
            catch (Exception)
            {
                // Commit acknowledgement can be lost after PostgreSQL has made the row durable.
                // A fresh store query is the authority; never compensate until it proves no row exists.
                try
                {
                    var completed = await store.FindCompletedByAttemptAsync(ticket.Id, owner, processingLeaseId, masterKey, thumbnailKey, CancellationToken.None);
                    if (completed is not null)
                    {
                        await DeleteBestEffortAsync($"staging/{owner:D}/", ticket.StagingObjectKey);
                        return Dto(completed.Id);
                    }
                }
                catch (Exception reconciliationException)
                {
                    throw new UploadCommitOutcomeUnknownException(reconciliationException);
                }
                throw;
            }
            await DeleteBestEffortAsync($"staging/{owner:D}/", ticket.StagingObjectKey);
            return Dto(image.Id);
        }
        catch (OperationCanceledException) { await ReleaseAsync(ticket, owner, processingLeaseId, masterKey, thumbnailKey, cancellationToken); throw; }
        catch (UploadCommitOutcomeUnknownException) { throw; }
        catch (BusinessException) { await FailAndCleanAsync(ticket, owner, processingLeaseId, masterKey, thumbnailKey, CancellationToken.None); throw; }
        catch (InvalidDataException) { await FailAndCleanAsync(ticket, owner, processingLeaseId, masterKey, thumbnailKey, CancellationToken.None); throw InvalidImage(); }
        catch
        {
            await ReleaseAsync(ticket, owner, processingLeaseId, masterKey, thumbnailKey, cancellationToken);
            throw;
        }
    }
    private async Task<byte[]> ReadBoundedAsync(Stream source, long declared, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream(capacity: 5_000_001);
        var bytes = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(bytes, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > 5_000_000) throw TooLarge();
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length != declared) throw InvalidImage();
        return buffer.ToArray();
    }
    private static ExerciseImageDto Dto(Guid id) => new(id, $"/api/v1/media/exercise-images/{id:D}/master", $"/api/v1/media/exercise-images/{id:D}/thumbnail");
    private static BusinessException Missing() => new(BusinessErrorCode.ExerciseNotFound, "The exercise was not found.", 404);
    private static BusinessException Processing() => new(BusinessErrorCode.VersionConflict, "The image upload is already being processed.", 409);
    private static BusinessException InvalidImage() => new(BusinessErrorCode.ImageTypeNotSupported, "The image type is not supported.", 400);
    private static BusinessException TooLarge() => new(BusinessErrorCode.ImageTooLarge, "The image is too large.", 400);
    private async Task FailAndCleanAsync(ImageUploadTicket ticket, Guid owner, Guid leaseId, string? master, string? thumbnail, CancellationToken ct)
    {
        var failed = await store.TryFailClaimAsync(ticket.Id, owner, leaseId, CancellationToken.None);
        var cleaned = await CleanupAttemptAsync(owner, master, thumbnail, CancellationToken.None);
        if (failed && await DeleteBestEffortAsync($"staging/{owner:D}/", ticket.StagingObjectKey)) cleaned = true;
        if (failed && cleaned) await store.MarkCleanupCompleteAsync(ticket.Id, ticket.StagingObjectKey, leaseId, CancellationToken.None);
    }
    private async Task ReleaseAsync(ImageUploadTicket ticket, Guid owner, Guid leaseId, string? master, string? thumbnail, CancellationToken ct)
    {
        var cleaned = await CleanupAttemptAsync(owner, master, thumbnail, CancellationToken.None);
        // A failed synchronous cleanup durably retains the lease marker before the retryable claim is released.
        _ = await store.TryReleaseClaimAsync(ticket.Id, owner, leaseId, CancellationToken.None, retainAttemptForCleanup: !cleaned);
    }
    private async Task<bool> CleanupAttemptAsync(Guid owner, string? master, string? thumbnail, CancellationToken ct)
    { var success = true; try { if (master is not null) await storage.DeleteAsync($"private/{owner:D}/", master, ct); } catch { success = false; } try { if (thumbnail is not null) await storage.DeleteAsync($"private/{owner:D}/", thumbnail, ct); } catch { success = false; } return success; }
    private async Task<bool> DeleteBestEffortAsync(string prefix, string key) { try { await storage.DeleteAsync(prefix, key, CancellationToken.None); return true; } catch { return false; } }
}
