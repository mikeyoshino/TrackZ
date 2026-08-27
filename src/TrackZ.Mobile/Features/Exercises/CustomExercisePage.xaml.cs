using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Localization;
using System.Globalization;
using Microsoft.Maui.Storage;

namespace TrackZ.Mobile.Features.Exercises;

public partial class CustomExercisePage : ContentPage, IQueryAttributable
{
    private readonly CustomExerciseViewModel _viewModel;
    private readonly LocalExerciseImageSelectionCoordinator _imageSelection;
    private readonly IFileSystem _fileSystem;
    private readonly MobileTextSet _text;

    public CustomExercisePage(
        CustomExerciseViewModel viewModel,
        LocalExerciseImageSelectionCoordinator imageSelection,
        IFileSystem fileSystem)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _text = viewModel.Text;
        _imageSelection = imageSelection;
        _fileSystem = fileSystem;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("exerciseId", out var value) && Guid.TryParse(value?.ToString(), out var id))
        {
            _ = LoadForEditAsync(id);
            return;
        }

        if (query.TryGetValue("suggestedName", out var suggestedName))
        {
            var normalizedName = Uri.UnescapeDataString(
                Convert.ToString(suggestedName) ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(normalizedName))
                _viewModel.Name = normalizedName;
        }
    }

    private async void OnChooseImageClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _imageSelection.PickAndSelectAsync(
                _text.ChooseExerciseImage,
                Path.Combine(_fileSystem.AppDataDirectory, "exercise-images"),
                _viewModel.SelectLocalImage);
        }
        catch (UnsupportedExerciseImageException)
        {
            await DisplayAlertAsync(_text.ImageNotImported, _text.UnsupportedExerciseImage, _text.Okay);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync(_text.ImageNotImported, _text.ImageNotImported, _text.Okay);
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
            ? string.Format(CultureInfo.CurrentUICulture, _text.ExerciseSaveFailedFormat, _viewModel.LastErrorCode)
            : string.Join(Environment.NewLine, _viewModel.ValidationErrors.Values.SelectMany(messages => messages));
        await DisplayAlertAsync(_text.ExerciseNotSaved, message, _text.Okay);
    }

    private async Task LoadForEditAsync(Guid exerciseId)
    {
        await _viewModel.LoadForEditAsync(exerciseId);
    }
}
