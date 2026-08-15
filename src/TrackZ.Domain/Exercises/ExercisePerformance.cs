namespace TrackZ.Domain.Exercises;

/// <summary>
/// A materialized per-user exercise read model. Workout processing owns keeping it current.
/// </summary>
public sealed class ExercisePerformance
{
    private ExercisePerformance()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ExerciseDefinitionId { get; private set; }

    public DateTimeOffset? LastPerformedAt { get; private set; }

    public decimal? LastBestWeightKg { get; private set; }

    public decimal? LastBestAssistedKg { get; private set; }

    public int? LastBestReps { get; private set; }

    public decimal? AllTimeBestWeightKg { get; private set; }

    public decimal? AllTimeBestAssistedKg { get; private set; }

    public int? AllTimeBestReps { get; private set; }

    public static ExercisePerformance Create(
        Guid userId,
        Guid exerciseDefinitionId,
        DateTimeOffset lastPerformedAt,
        decimal? lastBestWeightKg,
        decimal? lastBestAssistedKg,
        int lastBestReps,
        decimal? allTimeBestWeightKg,
        decimal? allTimeBestAssistedKg,
        int allTimeBestReps)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(exerciseDefinitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(lastBestReps);
        ArgumentOutOfRangeException.ThrowIfNegative(allTimeBestReps);

        return new ExercisePerformance
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExerciseDefinitionId = exerciseDefinitionId,
            LastPerformedAt = lastPerformedAt.ToUniversalTime(),
            LastBestWeightKg = lastBestWeightKg,
            LastBestAssistedKg = lastBestAssistedKg,
            LastBestReps = lastBestReps,
            AllTimeBestWeightKg = allTimeBestWeightKg,
            AllTimeBestAssistedKg = allTimeBestAssistedKg,
            AllTimeBestReps = allTimeBestReps
        };
    }
}
