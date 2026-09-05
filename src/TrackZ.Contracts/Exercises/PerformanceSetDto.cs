namespace TrackZ.Contracts.Exercises;

public sealed record PerformanceSetDto
{
    public PerformanceSetDto(decimal? weightKg, decimal? assistedKg, int reps, int? plateCount = null)
    {
        WeightKg = weightKg;
        AssistedKg = assistedKg;
        Reps = reps;
        PlateCount = plateCount;
    }

    public decimal? WeightKg { get; }

    public decimal? AssistedKg { get; }

    public int Reps { get; }

    public int? PlateCount { get; }
}
