using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile.Features.Exercises;

public partial class CustomExercisePage : ContentPage, IQueryAttributable
{
    private readonly CustomExerciseViewModel _viewModel;
    private readonly LocalExerciseImageImporter _imageImporter;
    private readonly Data.ExerciseCache _cache;

    public CustomExercisePage(
        CustomExerciseViewModel viewModel,
        LocalExerciseImageImporter imageImporter,
        Data.ExerciseCache cache)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _imageImporter = imageImporter;
        _cache = cache;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("exerciseId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _ = LoadForEditAsync(id);
    }

    private async void OnChooseImageClicked(object? sender, EventArgs eventArgs)
    {
        var selected = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose an exercise image",
            FileTypes = FilePickerFileType.Images
        });
        if (selected is null) return;
        var contentType = ContentType(selected.FileName, selected.ContentType);
        if (contentType is null)
        {
            await DisplayAlertAsync("Unsupported image", "Choose a JPEG, PNG, or WebP image.", "OK");
            return;
        }
        var incoming = Path.Combine(FileSystem.CacheDirectory, $"exercise-import-{Guid.NewGuid():N}");
        try
        {
            await using (var input = await selected.OpenReadAsync())
            await using (var output = File.Create(incoming))
                await input.CopyToAsync(output);
            var imported = await _imageImporter.ImportAsync(
                incoming,
                contentType,
                Path.Combine(FileSystem.AppDataDirectory, "exercise-images"));
            _viewModel.SelectLocalImage(imported);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await DisplayAlertAsync("Image not imported", exception.Message, "OK");
        }
        finally
        {
            if (File.Exists(incoming)) File.Delete(incoming);
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

    private static string? ContentType(string fileName, string? reported) =>
        reported is "image/jpeg" or "image/png" or "image/webp"
            ? reported
            : Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => null
            };

    private async Task LoadForEditAsync(Guid exerciseId)
    {
        var exercise = (await _cache.GetAllAsync()).SingleOrDefault(item => item.Id == exerciseId && item.IsCustom);
        if (exercise is not null) _viewModel.LoadForEdit(exercise);
    }
}
