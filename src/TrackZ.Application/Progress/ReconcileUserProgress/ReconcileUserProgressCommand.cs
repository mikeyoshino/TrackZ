using MediatR;

namespace TrackZ.Application.Progress.ReconcileUserProgress;

public sealed record ReconcileUserProgressCommand(
    Guid UserId,
    Guid WorkoutId,
    IReadOnlyCollection<Guid> ExerciseDefinitionIds) : IRequest;

public interface IUserProgressReconciliationStore
{
    Task ReconcileAsync(
        Guid userId,
        Guid workoutId,
        IReadOnlyCollection<Guid> exerciseDefinitionIds,
        DateTimeOffset reconciledAt,
        CancellationToken cancellationToken);
}

public sealed class ReconcileUserProgressHandler(IUserProgressReconciliationStore store, TimeProvider timeProvider)
    : IRequestHandler<ReconcileUserProgressCommand>
{
    public Task Handle(ReconcileUserProgressCommand request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(request.UserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(request.WorkoutId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(request.ExerciseDefinitionIds);
        var affected = request.ExerciseDefinitionIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        return store.ReconcileAsync(
            request.UserId, request.WorkoutId, affected, timeProvider.GetUtcNow(), cancellationToken);
    }
}
