using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Exercises.Models;

public sealed class ExercisePickerItem : INotifyPropertyChanged
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly WorkoutTextSet _text;
    private readonly IWeightUnitPreference? _unitPreference;

    public ExercisePickerItem(
        CachedExercise exercise,
        WorkoutTextSet text,
        IWeightUnitPreference? unitPreference = null)
    {
        Exercise = exercise;
        _text = text;
        _unitPreference = unitPreference;
    }

    public CachedExercise Exercise { get; }
    public Guid Id => Exercise.Id;
    public string Name => Exercise.Name;
    public BodyPart BodyPart => Exercise.BodyPart;
    public TrackingMode TrackingMode => Exercise.TrackingMode;
    public string? ThumbnailUri => Exercise.ThumbnailUri;
    public Guid? LibraryImageId => Exercise.LibraryImageId;
    public DateTimeOffset? LastPerformedAt => Exercise.LastPerformedAt;
    public PerformanceSetDto? LastBestSet => Exercise.LastBestSet;
    public PerformanceSetDto? AllTimeBest => Exercise.AllTimeBest;
    public bool IsCustom => Exercise.IsCustom;
    public bool IsPendingSync => Exercise.IsPendingSync;
    public DateTimeOffset LastSyncedAt => Exercise.LastSyncedAt;
    public string? SyncLabel => IsPendingSync ? _text.PendingSync : null;
    public string EditLabel => _text.Edit;
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

    private string FormatPerformance(PerformanceSetDto? performance)
    {
        if (performance is null) return "—";
        var reps = performance.Reps.ToString(CultureInfo.CurrentCulture);
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
