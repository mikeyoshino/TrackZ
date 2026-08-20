using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Train;

public partial class TrainPage : ContentPage
{
    private readonly TrainTodayViewModel _viewModel;
    private readonly IBodyAreaPicker _bodyAreaPicker;

    public TrainPage(TrainTodayViewModel viewModel, IBodyAreaPicker bodyAreaPicker)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _bodyAreaPicker = bodyAreaPicker;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnChooseWorkoutClicked(object? sender, EventArgs eventArgs)
    {
        var selected = await _bodyAreaPicker.PickAsync();
        if (selected is null) return;
        await Shell.Current.GoToAsync(
            $"{nameof(ExercisePickerPage)}?bodyPart={(int)selected.Value}");
    }

    private async void OnResumeClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("active-workout");
}
