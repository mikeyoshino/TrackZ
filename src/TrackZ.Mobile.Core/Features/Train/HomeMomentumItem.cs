using System.ComponentModel;
using System.Globalization;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Train;

public sealed class HomeMomentumItem : INotifyPropertyChanged, IDisposable
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly IWeightUnitPreference _preference;
    private readonly WorkoutTextSet _text;
    private bool _disposed;

    public HomeMomentumItem(
        ExerciseProgressSummaryDto source,
        IWeightUnitPreference preference,
        WorkoutTextSet text)
    {
        Source = source;
        _preference = preference;
        _text = text;
        _preference.Changed += OnPreferenceChanged;
    }

    public ExerciseProgressSummaryDto Source { get; }
    public string ExerciseName => Source.ExerciseName;
    public string LatestValueText => Format(Source.LastWeightKg, Source.LastAssistedKg, Source.LastReps, Source.LastPlateCount);
    public string BestValueText => Format(Source.BestWeightKg, Source.BestAssistedKg, Source.BestReps, Source.BestPlateCount);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preference.Changed -= OnPreferenceChanged;
    }

    private string Format(decimal? weightKg, decimal? assistedKg, int reps, int? plateCount)
    {
        if (plateCount is { } plates)
            return string.Format(CultureInfo.CurrentCulture, _text.PlateMeasurementFormat, plates, reps);
        return Source.TrackingMode switch
    {
        TrackingMode.Weighted => string.Format(
            CultureInfo.CurrentCulture,
            _text.HomeWeightedValueFormat,
            FormatNumber(weightKg),
            UnitLabel,
            reps),
        TrackingMode.Assisted => string.Format(
            CultureInfo.CurrentCulture,
            _text.HomeAssistedValueFormat,
            FormatNumber(assistedKg),
            UnitLabel,
            reps),
        TrackingMode.Bodyweight => string.Format(
            CultureInfo.CurrentCulture,
            _text.HomeBodyweightValueFormat,
            reps),
        _ => throw new ArgumentOutOfRangeException(nameof(Source.TrackingMode))
    };
    }

    private string UnitLabel => _preference.Current == WeightDisplayUnit.Pounds ? _text.Pounds : _text.Kilograms;

    private string FormatNumber(decimal? kilograms)
    {
        if (kilograms is null) return "—";
        return _preference.Current == WeightDisplayUnit.Pounds
            ? decimal.Round(kilograms.Value * PoundsPerKilogram, 2, MidpointRounding.AwayFromZero)
                .ToString("0.00", CultureInfo.CurrentCulture)
            : kilograms.Value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private void OnPreferenceChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatestValueText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BestValueText)));
    }
}
