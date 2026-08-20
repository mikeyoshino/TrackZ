using MediatR;

namespace TrackZ.Application.Progress.RebuildExercisePerformance;

public sealed record RebuildExercisePerformanceCommand(
    Guid UserId,
    IReadOnlyCollection<Guid> ExerciseDefinitionIds) : IRequest;

public interface IExercisePerformanceRebuildStore
{
    Task RecomputeExercisePerformancesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> exerciseDefinitionIds,
        CancellationToken cancellationToken);
}

public sealed class RebuildExercisePerformanceHandler(IExercisePerformanceRebuildStore store)
    : IRequestHandler<RebuildExercisePerformanceCommand>
{
    public Task Handle(
        RebuildExercisePerformanceCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(request.UserId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(request.ExerciseDefinitionIds);

        var affectedIds = request.ExerciseDefinitionIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();

        return affectedIds.Length == 0
            ? Task.CompletedTask
            : store.RecomputeExercisePerformancesAsync(
                request.UserId,
                affectedIds,
                cancellationToken);
    }
}
