using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile.Features.Exercises;

public sealed class CustomExerciseViewModel(CustomExerciseImageService service) : INotifyPropertyChanged
{
    private BusinessErrorCode? _lastErrorCode;

    public string Name { get; set; } = string.Empty;
    public BodyPart? BodyPart { get; set; }
    public TrackingMode? TrackingMode { get; set; }
    public Guid? LibraryImageId { get; set; }
    public string? LocalImagePath { get; set; }
    public string? LocalImageContentType { get; set; }
    public Guid? ExistingExerciseId { get; set; }
    public string? PreviewImagePath { get; set; }
    public IReadOnlyList<BodyPart> BodyParts { get; } = Enum.GetValues<BodyPart>();
    public IReadOnlyList<TrackingMode> TrackingModes { get; } = Enum.GetValues<TrackingMode>();
    public Dictionary<string, string[]> ValidationErrors { get; } = new(StringComparer.Ordinal);

    public BusinessErrorCode? LastErrorCode
    {
        get => _lastErrorCode;
        private set
        {
            if (_lastErrorCode == value) return;
            _lastErrorCode = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SelectLocalImage(ImportedExerciseImage image)
    {
        LibraryImageId = null;
        LocalImagePath = image.OriginalPath;
        LocalImageContentType = image.ContentType;
        PreviewImagePath = image.PreviewPath;
        OnPropertyChanged(nameof(PreviewImagePath));
    }

    public void SelectLibraryImage(Guid libraryImageId)
    {
        LibraryImageId = libraryImageId;
        LocalImagePath = null;
        LocalImageContentType = null;
        PreviewImagePath = null;
        OnPropertyChanged(nameof(PreviewImagePath));
    }

    public void LoadForEdit(CachedExercise exercise)
    {
        ExistingExerciseId = exercise.Id;
        Name = exercise.Name;
        BodyPart = exercise.BodyPart;
        TrackingMode = exercise.TrackingMode;
        PreviewImagePath = exercise.ThumbnailUri;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BodyPart));
        OnPropertyChanged(nameof(TrackingMode));
        OnPropertyChanged(nameof(PreviewImagePath));
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        if (ValidationErrors.Count != 0)
        {
            LastErrorCode = BusinessErrorCode.InvalidRequest;
            return false;
        }

        var draft = new CustomExerciseDraft(
            Name.Trim(),
            BodyPart!.Value,
            TrackingMode!.Value,
            LibraryImageId,
            LocalImagePath,
            LocalImageContentType,
            ExistingExerciseId,
            PreviewImagePath);
        try
        {
            await service.SaveAsync(draft, cancellationToken);
            LastErrorCode = null;
            return true;
        }
        catch (MobileApiException exception)
        {
            LastErrorCode = exception.ErrorCode;
            if (exception.FieldErrors is not null)
            {
                foreach (var error in exception.FieldErrors) ValidationErrors[error.Key] = error.Value;
            }
            return false;
        }
    }

    private void Validate()
    {
        ValidationErrors.Clear();
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100)
            ValidationErrors["name"] = ["Enter an exercise name of 100 characters or fewer."];
        if (BodyPart is not { } bodyPart || !Enum.IsDefined(bodyPart))
            ValidationErrors["bodyPart"] = ["Choose a body part."];
        if (TrackingMode is not { } mode || !Enum.IsDefined(mode))
            ValidationErrors["trackingMode"] = ["Choose a tracking mode."];
        if (LibraryImageId is not null && LocalImagePath is not null)
            ValidationErrors["image"] = ["Choose either a library image or a local image."];
        if (LocalImagePath is not null && LocalImageContentType is not ("image/jpeg" or "image/png" or "image/webp"))
            ValidationErrors["image"] = ["Choose a JPEG, PNG, or WebP image."];
        if (LocalImagePath is null && LocalImageContentType is not null)
            ValidationErrors["image"] = ["The local image file is missing."];
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
