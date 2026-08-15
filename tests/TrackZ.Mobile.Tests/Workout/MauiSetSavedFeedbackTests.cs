using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class MauiSetSavedFeedbackTests
{
    [Fact]
    public async Task Account_cancellation_reaches_an_in_flight_motion_handler()
    {
        var sut = new MauiSetSavedFeedback(() => false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterPhaseCount = 0;
        sut.Saved += async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        sut.Saved += (_, _) =>
        {
            laterPhaseCount++;
            return Task.CompletedTask;
        };
        using var cancellation = new CancellationTokenSource();

        var feedback = sut.SetSavedAsync(new LocalSet(50m, null, 10), cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => feedback.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, laterPhaseCount);
    }
}
