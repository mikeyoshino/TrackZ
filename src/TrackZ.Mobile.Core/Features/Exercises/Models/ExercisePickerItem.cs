using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises.Models;

public sealed class ExercisePickerItem : INotifyPropertyChanged
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly WorkoutTextSet _text;
    private readonly IWeightUnitPreference? _unitPreference;
    private readonly Func<ExercisePickerItem, Task<string?>>? _retryArtwork;
    private readonly Action<Guid, ExerciseArtworkState>? _artworkStateChanged;
    private string? _thumbnailUri;
    private ExerciseArtworkState _artworkState;

    public ExercisePickerItem(
        CachedExercise exercise,
        WorkoutTextSet text,
        IWeightUnitPreference? unitPreference = null,
        Func<ExercisePickerItem, Task<string?>>? retryArtwork = null,
        Action<Guid, ExerciseArtworkState>? artworkStateChanged = null,
        Action<ExercisePickerItem>? toggleSelection = null)
    {
        Exercise = exercise;
        _text = text;
        _unitPreference = unitPreference;
        _retryArtwork = retryArtwork;
        _artworkStateChanged = artworkStateChanged;
        _thumbnailUri = exercise.ThumbnailUri;
        _artworkState = string.IsNullOrWhiteSpace(_thumbnailUri)
            ? ExerciseArtworkState.Unavailable
            : ExerciseArtworkState.Ready;
        RetryArtworkCommand = new AsyncCommand(
            RetryArtworkAsync,
            _ => _retryArtwork is not null && Exercise.RemoteThumbnailRoute is not null);
        ToggleSelectionCommand = new RelayCommand(
            _ => toggleSelection?.Invoke(this),
            _ => toggleSelection is not null);
    }

    public CachedExercise Exercise { get; }
    public Guid Id => Exercise.Id;
    public string Name => Exercise.Name;
    public BodyPart BodyPart => Exercise.BodyPart;
    public TrackingMode TrackingMode => Exercise.TrackingMode;
    public string? ThumbnailUri => _thumbnailUri;
    public string? RemoteThumbnailRoute => Exercise.RemoteThumbnailRoute;
    public ExerciseArtworkState ArtworkState => _artworkState;
    public bool HasArtwork => ArtworkState == ExerciseArtworkState.Ready
        && !string.IsNullOrWhiteSpace(ThumbnailUri);
    public bool ShowsArtworkPlaceholder => !HasArtwork;
    public bool HasFailedArtwork => ArtworkState == ExerciseArtworkState.Failed;
    public bool IsArtworkLoading => ArtworkState == ExerciseArtworkState.Loading;
    public IAsyncCommand RetryArtworkCommand { get; }
    public ICommand ToggleSelectionCommand { get; }
    public Guid? LibraryImageId => Exercise.LibraryImageId;
    public DateTimeOffset? LastPerformedAt => Exercise.LastPerformedAt;
    public PerformanceSetDto? LastBestSet => Exercise.LastBestSet;
    public PerformanceSetDto? AllTimeBest => Exercise.AllTimeBest;
    public bool IsCustom => Exercise.IsCustom;
    public bool IsPendingSync => Exercise.IsPendingSync;
    public DateTimeOffset LastSyncedAt => Exercise.LastSyncedAt;
    public string? SyncLabel => IsPendingSync ? _text.PendingSync : null;
    public string EditLabel => _text.Edit;
    public string TrackingModeText => TrackingMode switch
    {
        TrackingMode.Weighted => _text.Weight,
        TrackingMode.Assisted => _text.Assistance,
        TrackingMode.Bodyweight => _text.Bodyweight,
        _ => string.Empty
    };
    public string ArtworkPlaceholderText => _text.ArtworkUnavailable;
    public string RetryArtworkText => _text.RetryArtwork;
    public string SelectionAccessibilityText => string.Format(
        CultureInfo.CurrentCulture,
        _text.SelectExerciseFormat,
        Name);
    public string ArtworkAccessibilityText => string.Format(
        CultureInfo.CurrentCulture,
        _text.ArtworkForExerciseFormat,
        Name);
    public string LastText => string.Format(
        CultureInfo.CurrentCulture, _text.PerformanceLastFormat, FormatPerformance(LastBestSet));
    public string PersonalRecordText => string.Format(
        CultureInfo.CurrentCulture, _text.PerformancePrFormat, FormatPerformance(AllTimeBest));

    public bool IsSelected
    {
        get => Exercise.IsSelected;
        set
        {
            if (Exercise.IsSelected == value) return;
            Exercise.IsSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshUnit()
    {
        OnPropertyChanged(nameof(LastText));
        OnPropertyChanged(nameof(PersonalRecordText));
    }

    internal void SetArtworkLoading() => SetArtworkState(ExerciseArtworkState.Loading);

    internal void SetArtworkFailed()
    {
        _thumbnailUri = null;
        OnPropertyChanged(nameof(ThumbnailUri));
        SetArtworkState(ExerciseArtworkState.Failed);
    }

    internal void SetArtworkReady(string localThumbnailUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localThumbnailUri);
        _thumbnailUri = localThumbnailUri;
        OnPropertyChanged(nameof(ThumbnailUri));
        SetArtworkState(ExerciseArtworkState.Ready);
    }

    internal void RestoreArtworkState(ExerciseArtworkState state)
    {
        if (state == ExerciseArtworkState.Ready && string.IsNullOrWhiteSpace(_thumbnailUri))
            state = ExerciseArtworkState.Unavailable;
        SetArtworkState(state);
    }

    private async Task RetryArtworkAsync(object? parameter)
    {
        if (_retryArtwork is null) return;
        SetArtworkLoading();
        try
        {
            var local = await _retryArtwork(this);
            if (string.IsNullOrWhiteSpace(local)) SetArtworkFailed();
            else SetArtworkReady(local);
        }
        catch (OperationCanceledException)
        {
            SetArtworkState(ExerciseArtworkState.Unavailable);
        }
        catch (Exception)
        {
            SetArtworkFailed();
        }
    }

    private void SetArtworkState(ExerciseArtworkState state)
    {
        if (_artworkState == state) return;
        _artworkState = state;
        _artworkStateChanged?.Invoke(Id, state);
        OnPropertyChanged(nameof(ArtworkState));
        OnPropertyChanged(nameof(HasArtwork));
        OnPropertyChanged(nameof(ShowsArtworkPlaceholder));
        OnPropertyChanged(nameof(HasFailedArtwork));
        OnPropertyChanged(nameof(IsArtworkLoading));
    }

    private string FormatPerformance(PerformanceSetDto? performance)
    {
        if (performance is null) return "—";
        var reps = performance.Reps.ToString(CultureInfo.CurrentCulture);
        if (performance.PlateCount is { } plates)
            return string.Format(
                CultureInfo.CurrentCulture,
                _text.PlateMeasurementFormat,
                plates,
                reps);
        return TrackingMode switch
        {
            TrackingMode.Weighted => string.Format(
                CultureInfo.CurrentCulture,
                _text.WeightedPerformanceFormat,
                FormatWeight(performance.WeightKg),
                UnitLabel,
                reps),
            TrackingMode.Assisted => string.Format(
                CultureInfo.CurrentCulture,
                _text.AssistedPerformanceFormat,
                FormatWeight(performance.AssistedKg),
                UnitLabel,
                reps),
            TrackingMode.Bodyweight => string.Format(
                CultureInfo.CurrentCulture,
                _text.BodyweightPerformanceFormat,
                reps,
                performance.Reps == 1 ? _text.RepSingular : _text.RepPlural),
            _ => "—"
        };
    }

    private WeightDisplayUnit DisplayUnit => _unitPreference?.Current ?? WeightDisplayUnit.Kilograms;

    private string UnitLabel => DisplayUnit == WeightDisplayUnit.Kilograms
        ? _text.Kilograms
        : _text.Pounds;

    private string FormatWeight(decimal? kilograms)
    {
        if (kilograms is null) return "—";
        return DisplayUnit == WeightDisplayUnit.Kilograms
            ? kilograms.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : decimal.Round(
                kilograms.Value * PoundsPerKilogram,
                2,
                MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.CurrentCulture);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
