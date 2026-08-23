using System.Globalization;
using System.Text.Json;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Workout;

public interface IExerciseGuidancePreferenceStore
{
    decimal? GetIncrementKg(Guid exerciseDefinitionId);
    void SetIncrementKg(Guid exerciseDefinitionId, decimal incrementKg);
    void Clear();
}

public static class WeightUnitConversion
{
    private const decimal PoundsPerKilogram = 2.204622621848775807m;

    public static decimal ToKilograms(decimal value, WeightDisplayUnit unit) =>
        unit == WeightDisplayUnit.Kilograms
            ? ValidateAndRound(value)
            : ValidateAndRound(value / PoundsPerKilogram);

    public static decimal FromKilograms(decimal kilograms, WeightDisplayUnit unit) =>
        unit == WeightDisplayUnit.Kilograms
            ? kilograms
            : decimal.Round(kilograms * PoundsPerKilogram, 2, MidpointRounding.AwayFromZero);

    private static decimal ValidateAndRound(decimal value)
    {
        if (value <= 0m) throw new ArgumentOutOfRangeException(nameof(value));
        return decimal.Round(value, SetMeasurement.MaximumKilogramScale, MidpointRounding.AwayFromZero);
    }
}

public sealed class ExerciseGuidancePreferenceStore(IWorkoutPreferenceStore store)
    : IExerciseGuidancePreferenceStore
{
    public const string PreferenceKey =
        "trackz_hypertrophy_progression_increments_v1";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public decimal? GetIncrementKg(Guid exerciseDefinitionId)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException(
                "Exercise ID cannot be empty.", nameof(exerciseDefinitionId));
        var values = ReadValidOrClear();
        return values.TryGetValue(exerciseDefinitionId.ToString("D"), out var value)
            ? Parse(value)
            : null;
    }

    public void SetIncrementKg(Guid exerciseDefinitionId, decimal incrementKg)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException(
                "Exercise ID cannot be empty.", nameof(exerciseDefinitionId));
        if (!Valid(incrementKg))
            throw new ArgumentOutOfRangeException(nameof(incrementKg));
        var values = ReadValidOrClear();
        values[exerciseDefinitionId.ToString("D")] =
            incrementKg.ToString("0.###", CultureInfo.InvariantCulture);
        store.Set(PreferenceKey, JsonSerializer.Serialize(values, JsonOptions));
    }

    public void Clear() => store.Set(PreferenceKey, "{}");

    private Dictionary<string, string> ReadValidOrClear()
    {
        try
        {
            var raw = store.Get(PreferenceKey);
            var values = string.IsNullOrWhiteSpace(raw)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonOptions)
                    ?? throw new JsonException("Increment preference is null.");
            foreach (var (key, value) in values)
            {
                if (!Guid.TryParseExact(key, "D", out var id)
                    || id == Guid.Empty
                    || !string.Equals(key, id.ToString("D"), StringComparison.Ordinal)
                    || Parse(value) is null)
                    throw new InvalidDataException("Increment preference is invalid.");
            }
            return values;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException
                or FormatException or OverflowException)
        {
            Clear();
            return [];
        }
    }

    private static decimal? Parse(string? value) =>
        decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed)
        && Valid(parsed)
            ? parsed
            : null;

    private static bool Valid(decimal value) =>
        value > 0m
        && value <= SetMeasurement.MaximumKilograms
        && DecimalScale(value) <= SetMeasurement.MaximumKilogramScale;

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0xff;
}
