using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class MauiSetSavedFeedbackTests
{
    [Fact]
    public async Task Account_cancellation_reaches_an_in_flight_motion_handler()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        using var lease = boundary.CreateCancellationLease(generation);
        var session = new SetSavedFeedbackSession(
            lease.Token,
            phase => boundary.TryStartSessionPhase(generation, phase, lease.Token));
        var sut = new MauiSetSavedFeedback(() => false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterPhaseCount = 0;
        sut.Saved += async (_, feedbackSession) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, feedbackSession.CancellationToken);
        };
        sut.Saved += (_, _) =>
        {
            laterPhaseCount++;
            return Task.CompletedTask;
        };
        var feedback = sut.SetSavedAsync(new LocalSet(50m, null, 10), session);
        await entered.Task;
        var reset = boundary.ResetAsync(_ => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => feedback.WaitAsync(TimeSpan.FromSeconds(2)));
        await reset.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, laterPhaseCount);
    }

    [Fact]
    public async Task Reset_winning_between_dispatch_and_actual_haptic_start_prevents_haptic()
    {
        var boundary = new AccountSessionBoundary();
        var generation = boundary.Capture();
        using var lease = boundary.CreateCancellationLease(generation);
        var session = new SetSavedFeedbackSession(
            lease.Token,
            phase => boundary.TryStartSessionPhase(generation, phase, lease.Token));
        var dispatch = new GatedMainThread();
        var hapticCount = 0;
        var sut = new MauiSetSavedFeedback(
            () => true,
            dispatch.InvokeAsync,
            () => hapticCount++);

        var feedback = sut.SetSavedAsync(new LocalSet(50m, null, 10), session);
        await dispatch.Entered.Task;
        var reset = boundary.ResetAsync(_ => Task.CompletedTask);
        await WaitForCancellationAsync(lease.Token);
        dispatch.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => feedback.WaitAsync(TimeSpan.FromSeconds(2)));
        await reset.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, hapticCount);
    }

    private static Task WaitForCancellationAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(completion.SetResult);
        return completion.Task;
    }

    private sealed class GatedMainThread
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task InvokeAsync(Action action)
        {
            Entered.TrySetResult();
            await Release.Task;
            action();
        }
    }
}
