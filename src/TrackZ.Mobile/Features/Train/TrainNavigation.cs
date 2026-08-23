using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Features.Train;

internal interface ITrainNavigationHost
{
    Task GoToAsync(string route, CancellationToken cancellationToken);
}

/// <summary>
/// The MAUI edge for Momentum Home navigation. Shell routes stay out of the Core view model so
/// command tests can exercise routing decisions without a platform shell.
/// </summary>
public sealed class MauiTrainNavigator : ITrainNavigator
{
    private readonly IBodyAreaPicker _bodyAreaPicker;
    private readonly ITrainNavigationHost _host;

    public MauiTrainNavigator(IBodyAreaPicker bodyAreaPicker)
        : this(bodyAreaPicker, new MauiTrainNavigationHost())
    {
    }

    internal MauiTrainNavigator(
        IBodyAreaPicker bodyAreaPicker,
        ITrainNavigationHost host)
    {
        _bodyAreaPicker = bodyAreaPicker;
        _host = host;
    }

    public async Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = await _bodyAreaPicker.PickAsync(cancellationToken);
        if (selected is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        await _host.GoToAsync(
            $"{nameof(ExercisePickerPage)}?bodyPart={(int)selected.Value}",
            cancellationToken);
    }

    public Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _host.GoToAsync("active-workout", cancellationToken);
    }

    public Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(exerciseId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();
        return _host.GoToAsync($"//progress?exerciseId={exerciseId:D}", cancellationToken);
    }

    private sealed class MauiTrainNavigationHost : ITrainNavigationHost
    {
        private static Shell CurrentShell =>
            Shell.Current ?? throw new InvalidOperationException("The application shell is unavailable.");

        public async Task GoToAsync(string route, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentShell.GoToAsync(route);
        }
    }
}
