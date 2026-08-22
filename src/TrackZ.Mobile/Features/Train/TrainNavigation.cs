using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Features.Train;

/// <summary>
/// The MAUI edge for Momentum Home navigation. Shell routes stay out of the Core view model so
/// command tests can exercise routing decisions without a platform shell.
/// </summary>
public sealed class MauiTrainNavigator(IBodyAreaPicker bodyAreaPicker) : ITrainNavigator
{
    public async Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = await bodyAreaPicker.PickAsync(cancellationToken);
        if (selected is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        await Shell.Current.GoToAsync(
            $"{nameof(ExercisePickerPage)}?bodyPart={(int)selected.Value}");
    }

    public async Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Shell.Current.GoToAsync("active-workout");
    }
}
