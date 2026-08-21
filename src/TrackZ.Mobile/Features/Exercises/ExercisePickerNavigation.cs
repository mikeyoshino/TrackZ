using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises;

public interface IExercisePickerNavigator
{
    Task ReturnToWorkoutAsync(CancellationToken cancellationToken = default);
}

internal interface IExercisePickerNavigationHost
{
    bool IsWorkoutImmediatelyBeforePicker { get; }
    Task PopPickerAsync(CancellationToken cancellationToken);
    Task OpenWorkoutAsync(CancellationToken cancellationToken);
}

public sealed class MauiExercisePickerNavigator : IExercisePickerNavigator
{
    private readonly IExercisePickerNavigationHost _host;

    public MauiExercisePickerNavigator() : this(new MauiExercisePickerNavigationHost())
    {
    }

    internal MauiExercisePickerNavigator(IExercisePickerNavigationHost host) => _host = host;

    public async Task ReturnToWorkoutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var returnsToExistingWorkout = _host.IsWorkoutImmediatelyBeforePicker;
        await _host.PopPickerAsync(cancellationToken);
        if (!returnsToExistingWorkout)
            await _host.OpenWorkoutAsync(cancellationToken);
    }

    private sealed class MauiExercisePickerNavigationHost : IExercisePickerNavigationHost
    {
        private static Shell CurrentShell =>
            Shell.Current ?? throw new InvalidOperationException("The application shell is unavailable.");

        public bool IsWorkoutImmediatelyBeforePicker
        {
            get
            {
                var stack = CurrentShell.Navigation.NavigationStack;
                return stack.Count >= 2 && stack[^2] is WorkoutPage;
            }
        }

        public async Task PopPickerAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentShell.GoToAsync("..");
        }

        public async Task OpenWorkoutAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentShell.GoToAsync("active-workout");
        }
    }
}
