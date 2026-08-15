using System.Windows.Input;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Features.Exercises;

public partial class ExercisePickerPage : ContentPage
{
    private readonly ExercisePickerViewModel _viewModel;

    public ExercisePickerPage(ExercisePickerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        EditCustomCommand = new Command<CachedExercise>(EditCustomExercise);
    }

    public event Action<IReadOnlyCollection<Guid>>? SelectionCompleted;
    public ICommand EditCustomCommand { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnCreateCustomClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(nameof(CustomExercisePage));

    private void OnClearBodyPartClicked(object? sender, EventArgs eventArgs) =>
        _viewModel.SelectedBodyPart = null;

    private async void OnDoneClicked(object? sender, EventArgs eventArgs)
    {
        var selected = _viewModel.SelectedExerciseIds.ToArray();
        SelectionCompleted?.Invoke(selected);
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
            await Shell.Current.GoToAsync("..");
    }

    private async void EditCustomExercise(CachedExercise exercise) =>
        await Shell.Current.GoToAsync($"{nameof(CustomExercisePage)}?exerciseId={exercise.Id:D}");
}
