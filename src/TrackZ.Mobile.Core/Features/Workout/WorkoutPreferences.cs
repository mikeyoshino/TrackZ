namespace TrackZ.Mobile.Features.Workout;

public interface IWorkoutPreferenceStore
{
    string? Get(string key);
    void Set(string key, string value);
}

public interface IWeightUnitPreference
{
    WeightDisplayUnit Current { get; }
    void Set(WeightDisplayUnit unit);
    event EventHandler? Changed;
}

public sealed class WeightUnitPreference(IWorkoutPreferenceStore store) : IWeightUnitPreference
{
    internal const string PreferenceKey = "trackz_weight_unit";

    public WeightDisplayUnit Current =>
        Enum.TryParse<WeightDisplayUnit>(store.Get(PreferenceKey), out var unit)
        && Enum.IsDefined(unit)
            ? unit
            : WeightDisplayUnit.Kilograms;

    public event EventHandler? Changed;

    public void Set(WeightDisplayUnit unit)
    {
        if (!Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        if (Current == unit) return;
        store.Set(PreferenceKey, unit.ToString());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
