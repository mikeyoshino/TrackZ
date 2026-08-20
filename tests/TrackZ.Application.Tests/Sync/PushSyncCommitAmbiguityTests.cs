using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Sync;
using TrackZ.Application.Sync.Push;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Sync;
using TrackZ.Domain.Workouts;
using TrackZ.Domain.Exercises;

namespace TrackZ.Application.Tests.Sync;

public sealed class PushSyncCommitAmbiguityTests
{
    [Fact]
    public async Task Transient_commit_acknowledgement_failure_is_not_reported_as_retryable()
    {
        var store = new CommitAcknowledgementLostStore();
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<ISyncPushStore>(store);
        services.AddSingleton<ICurrentUser>(new CurrentUser(Guid.NewGuid()));
        services.AddSingleton(TimeProvider.System);
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        var operation = new SyncOperationDto(
            Guid.NewGuid(),
            "Workout",
            "UnknownAction",
            JsonSerializer.SerializeToElement(new { }),
            0);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            sender.Send(new PushSyncCommand([operation])));

        Assert.True(store.Transaction.CommitAttempted);
        Assert.True(store.TrackingCleared);
    }

    private sealed record CurrentUser(Guid UserId) : ICurrentUser;

    private sealed class CommitAcknowledgementLostStore : ISyncPushStore
    {
        public CommitLostTransaction Transaction { get; } = new();
        public bool TrackingCleared { get; private set; }

        public Task<IAppDbTransaction> BeginSyncTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAppDbTransaction>(Transaction);

        public Task AcquireOperationLockAsync(
            Guid userId,
            Guid operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessedClientOperation?> FindProcessedOperationAsync(
            Guid userId,
            Guid operationId,
            CancellationToken cancellationToken) => Task.FromResult<ProcessedClientOperation?>(null);

        public void AddProcessedOperation(ProcessedClientOperation operation)
        {
        }

        public Task SaveSyncChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool IsTransient(Exception exception) => exception is TimeoutException;

        public void ClearSyncTracking() => TrackingCleared = true;

        public Task AcquireWorkoutLockAsync(Guid workoutId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkoutSession?> FindOwnedWorkoutAsync(
            Guid userId,
            Guid workoutId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> FindWorkoutOwnerAsync(Guid workoutId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, TrackingMode>> GetAvailableExerciseTrackingModesAsync(
            Guid userId,
            IReadOnlyList<Guid> exerciseDefinitionIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> AreWorkoutExerciseIdentifiersAvailableAsync(
            IReadOnlyList<Guid> workoutExerciseIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsSetIdentifierAvailableAsync(
            Guid setId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public void AddWorkout(WorkoutSession workout) => throw new NotSupportedException();

        public void AddSyncChange(SyncChange change) => throw new NotSupportedException();

        public Task RecomputeExercisePerformancesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> exerciseDefinitionIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CommitLostTransaction : IAppDbTransaction
    {
        public bool CommitAttempted { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitAttempted = true;
            throw new TimeoutException("The commit acknowledgement was lost.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
