using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Media;

public interface IExerciseImageUploadStore
{
    Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken);
    Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken);
    Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindOwnedImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken);
    Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    Task<UploadClaim> TryClaimUploadAsync(Guid ticketId, Guid ownerId, TimeSpan lease, CancellationToken cancellationToken);
    Task<StagingUploadTransition> TryMarkUploadedAsync(Guid ticketId, Guid ownerId, Guid uploadLeaseId, CancellationToken cancellationToken);
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
