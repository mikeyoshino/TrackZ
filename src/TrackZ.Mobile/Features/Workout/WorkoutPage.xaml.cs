using TrackZ.Mobile.Features.Exercises;
using System.Windows.Input;
using TrackZ.Mobile.Features.Summary;

namespace TrackZ.Mobile.Features.Workout;

public partial class WorkoutPage : ContentPage
{
    private readonly WorkoutViewModel _viewModel;

    public WorkoutPage(WorkoutViewModel viewModel, ExercisePickerPage exercisePicker)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        exercisePicker.SelectionCompleted += OnSelectionCompleted;
        _viewModel.WorkoutFinished += OnWorkoutFinished;
        _viewModel.WorkoutDiscarded += OnWorkoutDiscarded;
        OpenLoggerCommand = new Command<WorkoutExerciseDraftItem>(OpenLogger);
    }

    public ICommand OpenLoggerCommand { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RestoreAsync();
    }

    private async void OnAddExerciseClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(nameof(ExercisePickerPage));

    private Task OnSelectionCompleted(IReadOnlyCollection<Guid> selected) =>
        _viewModel.AddExercisesAsync(selected.ToArray());

    private async void OpenLogger(WorkoutExerciseDraftItem? exercise)
    {
        if (exercise is null || !_viewModel.HasStarted) return;
        var query = $"exerciseId={exercise.ExerciseDefinitionId:D}&name={Uri.EscapeDataString(exercise.Name)}";
        await Shell.Current.GoToAsync($"{nameof(SetLoggerPage)}?{query}");
    }

    private async void OnWorkoutFinished(object? sender, Guid workoutId) =>
        await Shell.Current.GoToAsync($"{nameof(WorkoutSummaryPage)}?workoutId={workoutId:D}");

    private async void OnWorkoutDiscarded(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//train");
}
