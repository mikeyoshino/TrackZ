using System.Globalization;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class ExerciseGuidancePreferenceStoreTests
{
    [Fact]
    public void Stores_each_increment_in_canonical_kilograms_and_survives_unit_changes()
    {
        var raw = new MemoryWorkoutPreferenceStore();
        var store = new ExerciseGuidancePreferenceStore(raw);
        var exerciseId = Guid.NewGuid();
        var canonical = WeightUnitConversion.ToKilograms(5m, WeightDisplayUnit.Pounds);

        store.SetIncrementKg(exerciseId, canonical);

        Assert.Equal(2.268m, store.GetIncrementKg(exerciseId));
        Assert.Equal(5.00m, WeightUnitConversion.FromKilograms(
            store.GetIncrementKg(exerciseId)!.Value, WeightDisplayUnit.Pounds));
        Assert.Equal(2.268m, WeightUnitConversion.FromKilograms(
            store.GetIncrementKg(exerciseId)!.Value, WeightDisplayUnit.Kilograms));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0001")]
    [InlineData("100000")]
    public void Rejects_invalid_canonical_increments(string value)
    {
        var store = new ExerciseGuidancePreferenceStore(new MemoryWorkoutPreferenceStore());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.SetIncrementKg(Guid.NewGuid(), decimal.Parse(value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Invalid_json_is_discarded_and_account_clear_removes_all_exercise_values()
    {
        var raw = new MemoryWorkoutPreferenceStore();
        raw.Set(ExerciseGuidancePreferenceStore.PreferenceKey, "not-json");
        var store = new ExerciseGuidancePreferenceStore(raw);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.Null(store.GetIncrementKg(first));
        store.SetIncrementKg(first, 2.5m);
        store.SetIncrementKg(second, 1.25m);
        store.Clear();

        Assert.Null(store.GetIncrementKg(first));
        Assert.Null(store.GetIncrementKg(second));
        Assert.Equal("{}", raw.Get(ExerciseGuidancePreferenceStore.PreferenceKey));
    }

    private sealed class MemoryWorkoutPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }
}
