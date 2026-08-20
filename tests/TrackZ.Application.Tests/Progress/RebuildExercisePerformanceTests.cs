using TrackZ.Application.Progress.RebuildExercisePerformance;

namespace TrackZ.Application.Tests.Progress;

public sealed class RebuildExercisePerformanceTests
{
    [Fact]
    public async Task Rebuild_normalizes_affected_exercise_ids_before_updating_the_projection()
    {
        var userId = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var lowerId = Guid.Parse("20000000-0000-0000-0000-000000000000");
        var higherId = Guid.Parse("30000000-0000-0000-0000-000000000000");
        var store = new RecordingPerformanceRebuildStore();
        var handler = new RebuildExercisePerformanceHandler(store);

        await handler.Handle(
            new RebuildExercisePerformanceCommand(
                userId,
                [higherId, Guid.Empty, lowerId, higherId]),
            CancellationToken.None);

        var rebuild = Assert.Single(store.Rebuilds);
        Assert.Equal(userId, rebuild.UserId);
        Assert.Equal([lowerId, higherId], rebuild.ExerciseIds);
    }

    private sealed class RecordingPerformanceRebuildStore : IExercisePerformanceRebuildStore
    {
        public List<(Guid UserId, IReadOnlyCollection<Guid> ExerciseIds)> Rebuilds { get; } = [];

        public Task RecomputeExercisePerformancesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> exerciseDefinitionIds,
            CancellationToken cancellationToken)
        {
            Rebuilds.Add((userId, exerciseDefinitionIds));
            return Task.CompletedTask;
        }
    }
}
