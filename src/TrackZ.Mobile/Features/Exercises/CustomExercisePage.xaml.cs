using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Localization;
using System.Globalization;
using Microsoft.Maui.Storage;
#if IOS
using UIKit;
#endif

namespace TrackZ.Mobile.Features.Exercises;

public partial class CustomExercisePage : ContentPage, IQueryAttributable
{
    private readonly CustomExerciseViewModel _viewModel;
    private readonly LocalExerciseImageSelectionCoordinator _imageSelection;
    private readonly ILocalExerciseImageCapture _imageCapture;
    private readonly IFileSystem _fileSystem;
    private readonly MobileTextSet _text;
    private readonly IExerciseOptionPicker _optionPicker;

    public CustomExercisePage(
        CustomExerciseViewModel viewModel,
        LocalExerciseImageSelectionCoordinator imageSelection,
        ILocalExerciseImageCapture imageCapture,
        IFileSystem fileSystem,
        IExerciseOptionPicker optionPicker)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _text = viewModel.Text;
        _imageSelection = imageSelection;
        _imageCapture = imageCapture;
        _fileSystem = fileSystem;
        _optionPicker = optionPicker;
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
        await SelectImageAsync(() => _imageSelection.PickAndSelectAsync(
            _text.ChooseExerciseImage,
            Path.Combine(_fileSystem.AppDataDirectory, "exercise-images"),
            _viewModel.SelectLocalImage));
    }

    private async void OnCaptureImageClicked(object? sender, EventArgs eventArgs)
    {
        await SelectImageAsync(() => _imageSelection.CaptureAndSelectAsync(
            _imageCapture,
            Path.Combine(_fileSystem.AppDataDirectory, "exercise-images"),
            _viewModel.SelectLocalImage));
    }

    private async Task SelectImageAsync(Func<Task<bool>> selectImage)
    {
        try
        {
            await selectImage();
        }
        catch (UnsupportedExerciseImageException)
        {
            await DisplayAlertAsync(_text.ImageNotImported, _text.UnsupportedExerciseImage, _text.Okay);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or PermissionException or FeatureNotSupportedException)
        {
            await DisplayAlertAsync(_text.ImageNotImported, _text.ImageNotImported, _text.Okay);
        }
    }

    internal void SetFormInputFocused(bool isFocused) =>
        CustomExerciseActions.IsVisible = !isFocused;

    private void OnFormInputFocused(object? sender, FocusEventArgs eventArgs) =>
        SetFormInputFocused(true);

    private void OnFormInputUnfocused(object? sender, FocusEventArgs eventArgs) =>
        SetFormInputFocused(false);

    private async void OnBodyPartClicked(object? sender, EventArgs eventArgs)
    {
        var selected = await _optionPicker.PickBodyPartAsync(
            _text.BodyPart,
            _viewModel.BodyPartOptions,
            _viewModel.SelectedBodyPart);
        if (selected is not null)
            _viewModel.SelectedBodyPart = selected;
    }

    private async void OnTrackingModeClicked(object? sender, EventArgs eventArgs)
    {
        var selected = await _optionPicker.PickTrackingModeAsync(
            _text.TrackingModeQuestion,
            _viewModel.TrackingModeOptions,
            _viewModel.SelectedTrackingMode);
        if (selected is not null)
            _viewModel.SelectedTrackingMode = selected;
    }

    private void OnNameInputHandlerChanged(object? sender, EventArgs eventArgs)
    {
#if IOS
        if (CustomExerciseNameInput.Handler?.PlatformView is UITextField input)
        {
            input.BorderStyle = UITextBorderStyle.None;
            input.BackgroundColor = UIColor.Clear;
        }
#endif
    }

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        if (SavingOverlay.IsVisible) return;
        CustomExerciseNameInput.Unfocus();
        SavingOverlay.IsVisible = true;
        try
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
        finally { SavingOverlay.IsVisible = false; }
    }

    private async Task LoadForEditAsync(Guid exerciseId)
    {
        await _viewModel.LoadForEditAsync(exerciseId);
    }
}
