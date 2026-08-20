using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Features.Workout;

public partial class WorkoutPage : ContentPage
{
    private readonly WorkoutViewModel _viewModel;

    public WorkoutPage(WorkoutViewModel viewModel, ExercisePickerPage exercisePicker)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        exercisePicker.SelectionCompleted += OnSelectionCompleted;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RestoreAsync();
    }

    private async void OnAddExerciseClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//exercises");

    private async void OnSelectionCompleted(IReadOnlyCollection<Guid> selected)
    {
        await _viewModel.AddExercisesAsync(selected.ToArray());
        await Shell.Current.GoToAsync("//train");
    }

    private async void OnOpenLoggerClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is not Button { CommandParameter: WorkoutExerciseDraftItem exercise }) return;
        var query = $"exerciseId={exercise.ExerciseDefinitionId:D}&name={Uri.EscapeDataString(exercise.Name)}";
        await Shell.Current.GoToAsync($"{nameof(SetLoggerPage)}?{query}");
    }
}
