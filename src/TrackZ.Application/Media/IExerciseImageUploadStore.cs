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
    Task<ExerciseImage> CommitCompletionAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, string masterKey, string thumbnailKey, CancellationToken cancellationToken);
    Task<bool> TryFailClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken);
    Task<bool> TryReleaseClaimAsync(Guid ticketId, Guid ownerId, Guid processingLeaseId, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}

public enum StagingUploadTransition
{
    Uploaded,
    RetainedByAnotherUpload,
    Rejected
}
