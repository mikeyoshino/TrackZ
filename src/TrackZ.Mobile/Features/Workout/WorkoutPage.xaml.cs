using TrackZ.Mobile.Features.Exercises;
using System.Windows.Input;
using TrackZ.Mobile.Features.Summary;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Workout;

public partial class WorkoutPage : ContentPage
{
    private readonly WorkoutViewModel _viewModel;
    private readonly IBodyAreaPicker _bodyAreaPicker;
    private bool _addingExercise;
    private readonly INativeSheetPresenter _sheetPresenter;
    private bool _showingEmptyPrompt;

    public WorkoutPage(WorkoutViewModel viewModel, ExercisePickerPage exercisePicker, IBodyAreaPicker bodyAreaPicker, INativeSheetPresenter sheetPresenter)
    {
        _sheetPresenter = sheetPresenter;
        _bodyAreaPicker = bodyAreaPicker;
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        exercisePicker.SelectionCompleted += OnSelectionCompleted;
        _viewModel.ExerciseLoggingRequested += OnExerciseLoggingRequested;
        _viewModel.WorkoutFinished += OnWorkoutFinished;
        _viewModel.WorkoutDiscarded += OnWorkoutDiscarded;
        _viewModel.EmptyWorkoutStartRequested += OnEmptyWorkoutStartRequested;
        OpenLoggerCommand = new Command<WorkoutExerciseDraftItem>(OpenLogger);
    }

    public ICommand OpenLoggerCommand { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RestoreAsync();
    }

    private async void OnAddExerciseClicked(object? sender, EventArgs eventArgs) => await AddExerciseAsync();

    private async Task AddExerciseAsync()
    {
        if (_addingExercise) return;
        _addingExercise = true;
        try
        {
            var selection = await _bodyAreaPicker.PickForAddingAsync();
            if (!selection.Confirmed) return;
            var category = selection.BodyPart.HasValue ? ((int)selection.BodyPart.Value).ToString() : "all";
            await Shell.Current.GoToAsync($"{nameof(ExercisePickerPage)}?bodyPart={category}");
        }
        finally { _addingExercise = false; }
    }

    private async void OnEmptyWorkoutStartRequested(object? sender, EventArgs args)
    {
        if (_showingEmptyPrompt) return;
        _showingEmptyPrompt = true;
        EmptyWorkoutSheetPage? sheet = null;
        sheet = new EmptyWorkoutSheetPage(_viewModel.Text, async add =>
        {
            await _sheetPresenter.DismissAsync(sheet!);
            _showingEmptyPrompt = false;
            if (add) await AddExerciseAsync();
        });
        try { await _sheetPresenter.ShowAsync(sheet, NativeSheetDetent.Compact); }
        catch { _showingEmptyPrompt = false; throw; }
    }

    private async void OnWorkoutMenuClicked(object? sender, EventArgs eventArgs)
    {
        var choice = await DisplayActionSheetAsync(_viewModel.Text.WorkoutTitle,
            _viewModel.Text.Cancel, _viewModel.HasWorkoutToDiscard ? _viewModel.Text.DiscardWorkout : null,
            _viewModel.HasStarted ? new[] { _viewModel.Text.FinishWorkout } : Array.Empty<string>());
        if (choice == _viewModel.Text.DiscardWorkout)
            _viewModel.DiscardWorkoutCommand.Execute(null);
        else if (choice == _viewModel.Text.FinishWorkout)
            _viewModel.FinishWorkoutCommand.Execute(null);
    }

    private Task OnSelectionCompleted(IReadOnlyCollection<Guid> selected) =>
        _viewModel.AddExercisesAsync(selected.ToArray());

    private async void OpenLogger(WorkoutExerciseDraftItem? exercise)
    {
        if (exercise is null || !_viewModel.HasStarted) return;
        var query = $"exerciseId={exercise.ExerciseDefinitionId:D}&name={Uri.EscapeDataString(exercise.Name)}";
        await Shell.Current.GoToAsync($"{nameof(SetLoggerPage)}?{query}");
    }

    private void OnExerciseLoggingRequested(object? sender, WorkoutExerciseDraftItem exercise) =>
        OpenLogger(exercise);

    private async void OnWorkoutFinished(object? sender, Guid workoutId) =>
        await Shell.Current.GoToAsync($"{nameof(WorkoutSummaryPage)}?workoutId={workoutId:D}");

    private async void OnWorkoutDiscarded(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//train");
}
