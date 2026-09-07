using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises;

public interface IExercisePickerNavigator
{
    Task OpenCustomExerciseAsync(string suggestedName, CancellationToken cancellationToken = default);
    Task OpenTechniqueAsync(Guid exerciseId, CancellationToken cancellationToken = default);
    Task ReturnToWorkoutAsync(CancellationToken cancellationToken = default);
}

internal interface IExercisePickerNavigationHost
{
    bool IsWorkoutImmediatelyBeforePicker { get; }
    Task OpenCustomExerciseAsync(string route, CancellationToken cancellationToken);
    Task OpenTechniqueAsync(string route, CancellationToken cancellationToken);
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

    public Task OpenCustomExerciseAsync(
        string suggestedName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedName = suggestedName.Trim();
        var route = nameof(CustomExercisePage);
        if (normalizedName.Length > 0)
            route += $"?suggestedName={Uri.EscapeDataString(normalizedName)}";
        return _host.OpenCustomExerciseAsync(route, cancellationToken);
    }

    public Task OpenTechniqueAsync(Guid exerciseId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exerciseId == Guid.Empty)
            throw new ArgumentException("An exercise is required.", nameof(exerciseId));
        return _host.OpenTechniqueAsync(
            $"{nameof(ExerciseTechniquePage)}?exerciseId={exerciseId:D}",
            cancellationToken);
    }

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

        public async Task OpenCustomExerciseAsync(string route, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentShell.GoToAsync(route);
        }

        public async Task OpenTechniqueAsync(string route, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentShell.GoToAsync(route);
        }

        public async Task OpenWorkoutAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentShell.GoToAsync("active-workout");
        }
    }
}
