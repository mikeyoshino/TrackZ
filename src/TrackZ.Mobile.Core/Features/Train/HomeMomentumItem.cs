using System.ComponentModel;
using System.Globalization;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Train;

public sealed class HomeMomentumItem : INotifyPropertyChanged, IDisposable
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;
    private readonly IWeightUnitPreference _preference;
    private readonly GamificationTextSet _text;
    private bool _disposed;

    public HomeMomentumItem(
        ExerciseProgressSummaryDto source,
        IWeightUnitPreference preference,
        GamificationTextSet text)
    {
        Source = source;
        _preference = preference;
        _text = text;
        _preference.Changed += OnPreferenceChanged;
    }

    public ExerciseProgressSummaryDto Source { get; }
    public string ExerciseName => Source.ExerciseName;
    public string LastText => Format(_text.Last, Source.LastWeightKg, Source.LastAssistedKg, Source.LastReps);
    public string BestText => Format(_text.Best, Source.BestWeightKg, Source.BestAssistedKg, Source.BestReps);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preference.Changed -= OnPreferenceChanged;
    }

    private string Format(string label, decimal? weightKg, decimal? assistedKg, int reps) => Source.TrackingMode switch
    {
        TrackingMode.Weighted => $"{label} {FormatWeight(weightKg)} × {reps}",
        TrackingMode.Assisted => $"{label} {FormatWeight(assistedKg)} {_text.Assistance} × {reps}",
        TrackingMode.Bodyweight => $"{label} {reps} {_text.Reps.ToLower(CultureInfo.CurrentCulture)}",
        _ => throw new ArgumentOutOfRangeException(nameof(Source.TrackingMode))
    };

    private string FormatWeight(decimal? kilograms)
    {
        if (kilograms is null) return "—";
        return _preference.Current == WeightDisplayUnit.Pounds
            ? $"{decimal.Round(kilograms.Value * PoundsPerKilogram, 2, MidpointRounding.AwayFromZero):0.00} {_text.Pounds}"
            : $"{kilograms.Value:0.###} {_text.Kilograms}";
    }

    private void OnPreferenceChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BestText)));
    }
}
