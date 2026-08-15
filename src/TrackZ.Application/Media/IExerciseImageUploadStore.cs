using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Media;

public interface IExerciseImageUploadStore
{
    Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken);
    Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken);
    Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    /// <summary>Reads the current durable ticket without reusing a tracked request snapshot.</summary>
    Task<ImageUploadTicket?> FindOwnedTicketSnapshotAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindOwnedImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindReadableImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken);
    /// <summary>Rechecks image eligibility after a signed capability has been validated.</summary>
    Task<ExerciseImage?> FindSignedReadableImageAsync(Guid imageId, CancellationToken cancellationToken);
    Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    Task<UploadClaim> TryClaimUploadAsync(Guid ticketId, Guid ownerId, TimeSpan lease, CancellationToken cancellationToken);
    /// <summary>
    /// Accepts the lease-scoped upload. Throws <see cref="UploadTransitionCommitAmbiguousException"/>
    /// only after entering transaction commit; any other exception is a definite pre-commit failure.
    /// </summary>
    Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, Guid uploadLeaseId, CancellationToken cancellationToken);
    /// <summary>Reconciles a possibly committed content transition against the exact accepted staging contract.</summary>
    Task<bool> IsAcceptedUploadAttemptDurableAsync(
        Guid ticketId,
        Guid ownerId,
        string stagingObjectKey,
        string contentType,
        long length,
        CancellationToken cancellationToken);
    Task<ExerciseImage> CommitCompletionAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken);
    /// <summary>Reads a fresh, durable completion outcome after an ambiguous commit failure.</summary>
    Task<ExerciseImage?> FindCompletedByAttemptAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImageUploadCleanupCandidate>> ListCleanupCandidatesAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<ImageUploadCleanupCandidate?> TryClaimCleanupAsync(ImageUploadCleanupCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CompleteCleanupClaimAsync(Guid ticketId, Guid cleanupClaimId, CancellationToken cancellationToken);
    Task ReleaseCleanupClaimAsync(Guid ticketId, Guid cleanupClaimId, CancellationToken cancellationToken);
    Task MarkCleanupCompleteAsync(Guid ticketId, string? stagingKey, Guid? processingLeaseId, CancellationToken cancellationToken);
    Task<bool> TryFailClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken);
    Task<bool> TryReleaseClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken, bool retainAttemptForCleanup = false);
    Task SaveAsync(CancellationToken cancellationToken);
}

public sealed record UploadClaim(Guid UploadLeaseId, string StagingObjectKey, DateTimeOffset ExpiresAt);
public sealed record ImageUploadCleanupCandidate(Guid TicketId, Guid OwnerId, Guid ExerciseId, ImageUploadState State, string? StagingKey, Guid? ProcessingLeaseId, Guid? CleanupClaimId = null);

public enum StagingUploadTransition
{
    Uploaded,
    RetainedByAnotherUpload,
    Rejected
}
