using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Media;

public interface IExerciseImageUploadStore
{
    Task<ExerciseDefinition?> FindOwnedActiveExerciseAsync(Guid exerciseId, Guid ownerId, CancellationToken cancellationToken);
    Task AddTicketAsync(ImageUploadTicket ticket, CancellationToken cancellationToken);
    Task<ImageUploadTicket?> FindOwnedTicketAsync(Guid ticketId, Guid ownerId, CancellationToken cancellationToken);
    Task<ExerciseImage?> FindImageAsync(Guid imageId, CancellationToken cancellationToken);
    Task<int> NextImageVersionAsync(Guid exerciseId, CancellationToken cancellationToken);
    Task AddImageAsync(ExerciseImage image, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
}
