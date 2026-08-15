namespace TrackZ.Contracts.Exercises;

public sealed record PerformanceSetDto
{
    public PerformanceSetDto(decimal? weightKg, decimal? assistedKg, int reps)
    {
        WeightKg = weightKg;
        AssistedKg = assistedKg;
        Reps = reps;
    }

    public decimal? WeightKg { get; }

    public decimal? AssistedKg { get; }

    public int Reps { get; }
}
