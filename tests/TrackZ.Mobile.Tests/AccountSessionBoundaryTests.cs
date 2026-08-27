using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests;

public sealed class AccountSessionBoundaryTests
{
    [Fact]
    public async Task Declined_conditional_reset_holds_old_writes_then_resumes_without_cleanup()
    {
        var boundary = new AccountSessionBoundary();
        var oldGeneration = boundary.Capture();
        var authorizationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthorization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        var oldMutationRan = false;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var reset = boundary.TryResetIfAsync(
            oldGeneration,
            async _ =>
            {
                authorizationEntered.TrySetResult();
                await releaseAuthorization.Task;
                return false;
            },
            _ =>
            {
                cleanupCount++;
                return Task.CompletedTask;
            });
        await authorizationEntered.Task;
        var oldMutation = boundary.TryCommitAsync(oldGeneration, _ =>
        {
            oldMutationRan = true;
            return Task.CompletedTask;
        });

        Assert.False(oldMutation.IsCompleted);
        releaseAuthorization.TrySetResult();

        Assert.False(await reset);
        Assert.True(await oldMutation);
        Assert.True(oldMutationRan);
        Assert.Equal(0, cleanupCount);
        Assert.Equal(0, resetNotificationCount);
        Assert.Equal(oldGeneration, boundary.Capture());
        Assert.True(await boundary.TryCommitAsync(
            boundary.Capture(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Conditional_reset_authorization_failure_preserves_generation_and_queued_writes()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        var authorizationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthorization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("confirmation failed");
        var mutationRan = false;
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var reset = boundary.TryResetIfAsync(
            generation,
            async _ =>
            {
                authorizationEntered.TrySetResult();
                await releaseAuthorization.Task;
                throw failure;
            },
            _ =>
            {
                cleanupCount++;
                return Task.CompletedTask;
            });
        await authorizationEntered.Task;
        var mutation = boundary.TryCommitAsync(generation, _ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        });
        Assert.False(mutation.IsCompleted);

        releaseAuthorization.TrySetResult();

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => reset));
        Assert.True(await mutation);
        Assert.True(mutationRan);
        Assert.Equal(generation, boundary.Capture());
        Assert.Equal(0, cleanupCount);
        Assert.Equal(0, resetNotificationCount);
    }

    [Fact]
    public async Task Cancellation_during_conditional_authorization_preserves_generation_cleanup_and_queued_writes()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        var authorizationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAuthorization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCancellation = new CancellationTokenSource();
        var mutationRan = false;
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var reset = boundary.TryResetIfAsync(
            generation,
            async _ =>
            {
                authorizationEntered.TrySetResult();
                await releaseAuthorization.Task;
                return true;
            },
            _ =>
            {
                cleanupCount++;
                return Task.CompletedTask;
            },
            callerCancellation.Token);
        await authorizationEntered.Task;
        var mutation = boundary.TryCommitAsync(generation, _ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        });
        Assert.False(mutation.IsCompleted);

        callerCancellation.Cancel();
        releaseAuthorization.TrySetResult();

        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reset);
        Assert.Equal(callerCancellation.Token, cancellation.CancellationToken);
        Assert.True(await mutation);
        Assert.True(mutationRan);
        Assert.Equal(generation, boundary.Capture());
        Assert.Equal(0, cleanupCount);
        Assert.Equal(0, resetNotificationCount);
    }

    [Fact]
    public async Task Cancellation_after_conditional_authorization_completes_reset_before_it_is_surfaced()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        using var callerCancellation = new CancellationTokenSource();
        var cleanupCount = 0;
        var resetNotificationCount = 0;
        boundary.SessionReset += (_, _) => resetNotificationCount++;

        var reset = boundary.TryResetIfAsync(
            generation,
            _ => Task.FromResult(true),
            _ =>
            {
                cleanupCount++;
                callerCancellation.Cancel();
                return Task.CompletedTask;
            },
            callerCancellation.Token);

        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reset);

        Assert.Equal(callerCancellation.Token, cancellation.CancellationToken);
        Assert.Equal(1, cleanupCount);
        Assert.Equal(1, resetNotificationCount);
        Assert.NotEqual(generation, boundary.Capture());
        Assert.False(await boundary.TryCommitAsync(generation, _ => Task.CompletedTask));
        Assert.True(await boundary.TryCommitAsync(
            boundary.Capture(), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Conditional_reset_cancellation_callback_can_observe_reset_without_deadlocking()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        using var lease = boundary.CreateCancellationLease(generation);
        var callbackRan = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lease.Token.Register(() =>
        {
            Assert.True(boundary.IsCancellationRequested(generation));
            callbackRan.TrySetResult();
        });

        Assert.True(await boundary.TryResetIfAsync(
            generation,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask));

        await callbackRan.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotEqual(generation, boundary.Capture());
    }

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
