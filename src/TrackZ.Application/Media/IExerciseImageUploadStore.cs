using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Media;

public interface IExerciseImageUploadStore
{
    Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken);
    Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken);
    Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindOwnedImageAsync(Guid imageId, Guid ownerId, CancellationToken cancellationToken);
    Task<ExerciseImage> CommitCompletionAsync(Guid ticketId, Guid ownerId, string masterKey, string thumbnailKey, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}
