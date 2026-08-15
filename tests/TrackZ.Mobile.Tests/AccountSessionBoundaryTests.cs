using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests;

public sealed class AccountSessionBoundaryTests
{
    [Fact]
    public async Task Cancellation_while_reset_waits_for_active_commit_restores_fresh_generation_before_it_is_surfaced()
    {
        var boundary = new AccountSessionBoundary();
        var oldGeneration = boundary.Capture();
        var commitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generationCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeCommit = boundary.TryCommitAsync(oldGeneration, async token =>
        {
            commitEntered.TrySetResult();
            using var registration = token.Register(() => generationCanceled.TrySetResult());
            await releaseCommit.Task;
            token.ThrowIfCancellationRequested();
        });
        await commitEntered.Task;
        using var callerCancellation = new CancellationTokenSource();
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var reset = boundary.ResetAsync(_ =>
        {
            cleanupCount++;
            return Task.CompletedTask;
        }, callerCancellation.Token);
        await generationCanceled.Task;
        callerCancellation.Cancel();

        Assert.False(reset.IsCompleted);
        releaseCommit.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeCommit);
        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reset);

        Assert.Equal(callerCancellation.Token, cancellation.CancellationToken);
        Assert.Equal(1, cleanupCount);
        Assert.Equal(1, resetNotificationCount);
        var freshGeneration = boundary.Capture();
        Assert.NotEqual(oldGeneration, freshGeneration);
        var staleMutationRan = false;
        Assert.False(await boundary.TryCommitAsync(oldGeneration, _ =>
        {
            staleMutationRan = true;
            return Task.CompletedTask;
        }));
        Assert.False(staleMutationRan);
        var freshMutationRan = false;
        Assert.True(await boundary.TryCommitAsync(freshGeneration, _ =>
        {
            freshMutationRan = true;
            return Task.CompletedTask;
        }));
        Assert.True(freshMutationRan);
    }

    [Fact]
    public async Task Cleanup_failure_is_propagated_after_notification_and_leaves_fresh_generation_usable()
    {
        var boundary = new AccountSessionBoundary();
        var oldGeneration = boundary.Capture();
        var failure = new InvalidOperationException("cleanup failed");
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ResetAsync(_ => Task.FromException(failure)));

        Assert.Same(failure, thrown);
        Assert.Equal(1, resetNotificationCount);
        Assert.NotEqual(oldGeneration, boundary.Capture());
        var mutationRan = false;
        Assert.True(await boundary.TryCommitAsync(boundary.Capture(), _ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        }));
        Assert.True(mutationRan);
    }

    [Fact]
    public async Task Pre_canceled_conditional_reset_does_not_invalidate_or_mutate_current_generation()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            boundary.TryResetAsync(generation, _ =>
            {
                cleanupCount++;
                return Task.CompletedTask;
            }, callerCancellation.Token));

        Assert.Equal(callerCancellation.Token, cancellation.CancellationToken);
        Assert.Equal(generation, boundary.Capture());
        Assert.Equal(0, cleanupCount);
        Assert.Equal(0, resetNotificationCount);
        var mutationRan = false;
        Assert.True(await boundary.TryCommitAsync(generation, _ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        }));
        Assert.True(mutationRan);
    }

    [Fact]
    public async Task Cancellation_callback_failure_cannot_prevent_cleanup_notification_or_fresh_commits()
    {
        var boundary = new AccountSessionBoundary();
        var oldGeneration = boundary.Capture();
        var callbackFailure = new InvalidOperationException("generation cancellation callback failed");
        using var lease = boundary.CreateCancellationLease(oldGeneration);
        using var registration = lease.Token.Register(() => throw callbackFailure);
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var thrown = await Assert.ThrowsAsync<AggregateException>(() =>
            boundary.ResetAsync(_ =>
            {
                cleanupCount++;
                return Task.CompletedTask;
            }));

        Assert.Contains(callbackFailure, thrown.Flatten().InnerExceptions);
        Assert.Equal(1, cleanupCount);
        Assert.Equal(1, resetNotificationCount);
        Assert.NotEqual(oldGeneration, boundary.Capture());
        Assert.True(await boundary.TryCommitAsync(boundary.Capture(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Throwing_reset_subscriber_does_not_skip_later_subscribers_or_strand_boundary()
    {
        var boundary = new AccountSessionBoundary();
        var notificationFailure = new InvalidOperationException("reset subscriber failed");
        var laterSubscriberRan = false;
        boundary.SessionReset += (_, _) => throw notificationFailure;
        boundary.SessionReset += (_, _) => laterSubscriberRan = true;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boundary.ResetAsync(_ => Task.CompletedTask));

        Assert.Same(notificationFailure, thrown);
        Assert.True(laterSubscriberRan);
        Assert.True(await boundary.TryCommitAsync(boundary.Capture(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Generation_cancellation_lease_remains_usable_until_released_after_reset()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        using var lease = boundary.CreateCancellationLease(generation);

        await boundary.ResetAsync(_ => Task.CompletedTask);

        Assert.True(lease.Token.IsCancellationRequested);
        using var staleLease = boundary.CreateCancellationLease(generation);
        Assert.True(staleLease.Token.IsCancellationRequested);
    }
}
