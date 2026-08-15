using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Exercises;

public sealed class CustomExerciseViewModel : INotifyPropertyChanged
{
    private readonly CustomExerciseImageService _service;
    private readonly ExerciseCache? _cache;
    private readonly IExerciseCatalogApi? _catalogApi;
    private readonly IConnectivityService? _connectivity;
    private readonly IExerciseThumbnailCache? _thumbnailCache;
    private readonly IAccountSessionBoundary _boundary;
    private BusinessErrorCode? _lastErrorCode;
    private CachedLibraryImage? _selectedLibraryImage;

    public CustomExerciseViewModel(
        CustomExerciseImageService service,
        ExerciseCache? cache = null,
        IExerciseCatalogApi? catalogApi = null,
        IConnectivityService? connectivity = null,
        IExerciseThumbnailCache? thumbnailCache = null,
        IAccountSessionBoundary? boundary = null)
    {
        _service = service;
        _cache = cache;
        _catalogApi = catalogApi;
        _connectivity = connectivity;
        _thumbnailCache = thumbnailCache;
        _boundary = boundary ?? new AccountSessionBoundary();
        _boundary.SessionReset += OnSessionReset;
    }

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
    public ObservableCollection<CachedLibraryImage> LibraryImages { get; } = [];

    public CachedLibraryImage? SelectedLibraryImage
    {
        get => _selectedLibraryImage;
        set
        {
            if (_selectedLibraryImage == value) return;
            _selectedLibraryImage = value;
            if (value is not null) SelectLibraryImage(value.ImageId, value.ThumbnailUri);
            OnPropertyChanged();
        }
    }

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

    public void SelectLibraryImage(Guid libraryImageId, string? localPreviewPath = null)
    {
        LibraryImageId = libraryImageId;
        LocalImagePath = null;
        LocalImageContentType = null;
        PreviewImagePath = localPreviewPath;
        OnPropertyChanged(nameof(PreviewImagePath));
    }

    public async Task LoadLibraryImagesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is null) return;
        var generation = _boundary.Capture();
        if (!await _boundary.TryCommitAsync(generation, async token =>
        {
            SetLibraryImages(await _cache.GetLibraryImagesAsync(token));
        }, cancellationToken)) return;
        if (_connectivity?.IsOnline != true || _catalogApi is null) return;
        try
        {
            var published = (await _catalogApi.GetAllAsync(cancellationToken))
                .Where(exercise => !exercise.IsCustom && exercise.LibraryImageId is not null)
                .ToArray();
            var metadata = published.Select(exercise =>
                new CachedLibraryImage(exercise.LibraryImageId!.Value, exercise.Name, null)).ToArray();
            if (!await _boundary.TryCommitAsync(generation, async token =>
            {
                await _cache.ReplaceLibraryImagesAsync(metadata, token);
                SetLibraryImages(metadata);
                LastErrorCode = null;
            }, cancellationToken)) return;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _boundary.GetCancellationToken(generation));
            await Parallel.ForEachAsync(
                published,
                new ParallelOptions { CancellationToken = linked.Token, MaxDegreeOfParallelism = 4 },
                async (exercise, token) =>
                {
                try
                {
                    var thumbnail = _thumbnailCache is null
                        ? null
                        : await _thumbnailCache.CacheAsync(exercise.ThumbnailUrl, token);
                    if (thumbnail is not null)
                        await _boundary.TryCommitAsync(generation, commitToken =>
                            _cache.SetLibraryThumbnailAsync(exercise.LibraryImageId!.Value, thumbnail, commitToken), token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Library metadata remains usable if one preview cannot be cached.
                }
                });
            await _boundary.TryCommitAsync(generation, async token =>
            {
                SetLibraryImages(await _cache.GetLibraryImagesAsync(token));
                LastErrorCode = null;
            }, cancellationToken);
        }
        catch (MobileApiException exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                LastErrorCode = exception.ErrorCode;
                return Task.CompletedTask;
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && _boundary.GetCancellationToken(generation).IsCancellationRequested)
        {
            // A session reset owns the UI reset and invalidates this refresh.
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                LastErrorCode = BusinessErrorCode.InternalServerError;
                return Task.CompletedTask;
            }, cancellationToken);
        }
    }

    public void LoadForEdit(CachedExercise exercise)
    {
        ExistingExerciseId = exercise.Id;
        Name = exercise.Name;
        BodyPart = exercise.BodyPart;
        TrackingMode = exercise.TrackingMode;
        LibraryImageId = exercise.LibraryImageId;
        PreviewImagePath = exercise.ThumbnailUri;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BodyPart));
        OnPropertyChanged(nameof(TrackingMode));
        OnPropertyChanged(nameof(PreviewImagePath));
    }

    public async Task<bool> LoadForEditAsync(
        Guid exerciseId,
        CancellationToken cancellationToken = default)
    {
        if (_cache is null) return false;
        var generation = _boundary.Capture();
        var loaded = false;
        var current = await _boundary.TryCommitAsync(generation, async token =>
        {
            var exercise = await _cache.FindExerciseAsync(exerciseId, token);
            if (exercise is null || !exercise.IsCustom) return;
            LoadForEdit(exercise);
            loaded = true;
        }, cancellationToken);
        return current && loaded;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        var generation = _boundary.Capture();
        CustomExerciseDraft? draft = null;
        if (!await _boundary.TryCommitAsync(generation, _ =>
        {
            Validate();
            if (ValidationErrors.Count != 0)
            {
                LastErrorCode = BusinessErrorCode.InvalidRequest;
                return Task.CompletedTask;
            }

            draft = new CustomExerciseDraft(
                Name.Trim(),
                BodyPart!.Value,
                TrackingMode!.Value,
                LibraryImageId,
                LocalImagePath,
                LocalImageContentType,
                ExistingExerciseId,
                PreviewImagePath);
            return Task.CompletedTask;
        }, cancellationToken)) return false;
        if (draft is null) return false;
        try
        {
            await _service.SaveAsync(draft, generation, cancellationToken);
            return await _boundary.TryCommitAsync(generation, _ =>
            {
                LastErrorCode = null;
                return Task.CompletedTask;
            }, cancellationToken);
        }
        catch (MobileApiException exception)
        {
            await _boundary.TryCommitAsync(generation, _ =>
            {
                LastErrorCode = exception.ErrorCode;
                if (exception.FieldErrors is not null)
                {
                    foreach (var error in exception.FieldErrors) ValidationErrors[error.Key] = error.Value;
                }
                return Task.CompletedTask;
            }, cancellationToken);
            return false;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && _boundary.GetCancellationToken(generation).IsCancellationRequested)
        {
            return false;
        }
    }

    private void SetLibraryImages(IEnumerable<CachedLibraryImage> images)
    {
        LibraryImages.Clear();
        foreach (var image in images) LibraryImages.Add(image);
    }

    private void OnSessionReset(object? sender, EventArgs eventArgs)
    {
        LibraryImages.Clear();
        _selectedLibraryImage = null;
        Name = string.Empty;
        BodyPart = null;
        TrackingMode = null;
        LibraryImageId = null;
        LocalImagePath = null;
        LocalImageContentType = null;
        PreviewImagePath = null;
        ExistingExerciseId = null;
        ValidationErrors.Clear();
        LastErrorCode = null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BodyPart));
        OnPropertyChanged(nameof(TrackingMode));
        OnPropertyChanged(nameof(SelectedLibraryImage));
        OnPropertyChanged(nameof(PreviewImagePath));
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
