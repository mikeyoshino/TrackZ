using TrackZ.Application.Progress.ReconcileUserProgress;

namespace TrackZ.Application.Tests.Progress;

public sealed class ReconcileUserProgressTests
{
    [Fact]
    public async Task Handler_normalizes_affected_exercises_and_delegates_one_reconciliation()
    {
        var userId = Guid.NewGuid();
        var workoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var store = new RecordingStore();
        var now = DateTimeOffset.Parse("2026-08-20T06:00:00Z");
        var handler = new ReconcileUserProgressHandler(store, new FixedTimeProvider(now));

        await handler.Handle(
            new ReconcileUserProgressCommand(userId, workoutId, [Guid.Empty, exerciseId, exerciseId]),
            CancellationToken.None);

        var call = Assert.Single(store.Calls);
        Assert.Equal(userId, call.UserId);
        Assert.Equal(workoutId, call.WorkoutId);
        Assert.Equal([exerciseId], call.ExerciseIds);
        Assert.Equal(now, call.ReconciledAt);
    }

    private sealed class RecordingStore : IUserProgressReconciliationStore
    {
        public List<(Guid UserId, Guid WorkoutId, IReadOnlyCollection<Guid> ExerciseIds, DateTimeOffset ReconciledAt)> Calls { get; } = [];
        public Task ReconcileAsync(Guid userId, Guid workoutId, IReadOnlyCollection<Guid> exerciseDefinitionIds, DateTimeOffset reconciledAt, CancellationToken cancellationToken)
        {
            Calls.Add((userId, workoutId, exerciseDefinitionIds, reconciledAt));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
