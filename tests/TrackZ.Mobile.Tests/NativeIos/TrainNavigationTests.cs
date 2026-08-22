using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Train;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class TrainNavigationTests
{
    [Fact]
    public async Task Production_adapter_routes_exact_active_workout_and_selected_body_part_paths()
    {
        var host = new RecordingTrainNavigationHost();
        var navigator = new MauiTrainNavigator(new FixedBodyAreaPicker(BodyPart.Back), host);

        await navigator.OpenActiveWorkoutAsync();
        await navigator.OpenWorkoutPickerAsync();

        Assert.Equal(
            ["active-workout", "ExercisePickerPage?bodyPart=2"],
            host.Routes);
    }

    [Fact]
    public async Task Production_adapter_preserves_cancellation_before_route_mutation()
    {
        var host = new RecordingTrainNavigationHost();
        var navigator = new MauiTrainNavigator(new FixedBodyAreaPicker(BodyPart.Chest), host);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => navigator.OpenActiveWorkoutAsync(cancellation.Token));

        Assert.Empty(host.Routes);
    }

    private sealed class FixedBodyAreaPicker(BodyPart? result) : IBodyAreaPicker
    {
        public Task<BodyPart?> PickAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingTrainNavigationHost : ITrainNavigationHost
    {
        public List<string> Routes { get; } = [];

        public Task GoToAsync(string route, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Routes.Add(route);
            return Task.CompletedTask;
        }
    }
}
