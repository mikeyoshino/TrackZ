namespace TrackZ.Domain.Workouts;

public sealed record SetMeasurement(decimal? WeightKg, decimal? AssistedKg, int Reps)
{
    public const decimal MinimumKilograms = 0.001m;
    public const decimal MaximumKilograms = 99999.999m;
    public const int MaximumKilogramScale = 3;
}
