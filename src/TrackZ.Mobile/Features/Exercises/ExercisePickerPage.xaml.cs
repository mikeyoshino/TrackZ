using System.Windows.Input;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises;

public partial class ExercisePickerPage : ContentPage, IQueryAttributable
{
    private readonly ExercisePickerViewModel _viewModel;
    private readonly IExercisePickerNavigator _navigator;
    private TrackZ.Domain.Exercises.BodyPart? _requestedBodyPart;

    public ExercisePickerPage(
        ExercisePickerViewModel viewModel,
        IExercisePickerNavigator navigator)
    {
        _viewModel = viewModel;
        _navigator = navigator;
        EditCustomCommand = new Command<CachedExercise>(EditCustomExercise);
        DoneCommand = new AsyncCommand(_ => CompleteSelectionAsync());
        InitializeComponent();
        BindingContext = _viewModel;
    }

    public event Func<IReadOnlyCollection<Guid>, Task>? SelectionCompleted;
    public ICommand EditCustomCommand { get; }
    public IAsyncCommand DoneCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("bodyPart", out var raw)) return;
        var value = Uri.UnescapeDataString(Convert.ToString(raw) ?? string.Empty);
        if (int.TryParse(value, out var number)
            && Enum.IsDefined(typeof(TrackZ.Domain.Exercises.BodyPart), number))
        {
            _requestedBodyPart = (TrackZ.Domain.Exercises.BodyPart)number;
            _viewModel.SelectedBodyPart = _requestedBodyPart;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync(_requestedBodyPart);
    }

    private async void OnCreateCustomClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync(nameof(CustomExercisePage));

    private async Task CompleteSelectionAsync()
    {
        var selected = _viewModel.SelectedExerciseIds.ToArray();
        var callbacks = SelectionCompleted?.GetInvocationList()
            .Cast<Func<IReadOnlyCollection<Guid>, Task>>()
            .ToArray() ?? [];
        foreach (var callback in callbacks)
            await callback(selected);
        await _navigator.ReturnToWorkoutAsync();
    }

    private async void EditCustomExercise(CachedExercise exercise) =>
        await Shell.Current.GoToAsync($"{nameof(CustomExercisePage)}?exerciseId={exercise.Id:D}");
}
