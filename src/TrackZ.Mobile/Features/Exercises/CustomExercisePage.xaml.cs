using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile.Features.Exercises;

public partial class CustomExercisePage : ContentPage, IQueryAttributable
{
    private readonly CustomExerciseViewModel _viewModel;
    private readonly LocalExerciseImageSelectionCoordinator _imageSelection;

    public CustomExercisePage(
        CustomExerciseViewModel viewModel,
        LocalExerciseImageSelectionCoordinator imageSelection)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _imageSelection = imageSelection;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("exerciseId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _ = LoadForEditAsync(id);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadLibraryImagesAsync();
    }

    private async void OnChooseImageClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _imageSelection.PickAndSelectAsync(
                Path.Combine(FileSystem.AppDataDirectory, "exercise-images"),
                _viewModel.SelectLocalImage);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync("Image not imported", exception.Message, "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        if (await _viewModel.SaveAsync())
        {
            await Shell.Current.GoToAsync("..");
            return;
        }
        var message = _viewModel.ValidationErrors.Count == 0
            ? $"Could not save the exercise ({_viewModel.LastErrorCode})."
            : string.Join(Environment.NewLine, _viewModel.ValidationErrors.Values.SelectMany(messages => messages));
        await DisplayAlertAsync("Exercise not saved", message, "OK");
    }

    private async Task LoadForEditAsync(Guid exerciseId)
    {
        await _viewModel.LoadForEditAsync(exerciseId);
    }
}
