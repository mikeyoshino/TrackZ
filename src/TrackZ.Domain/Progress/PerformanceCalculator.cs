using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Progress;

public static class PerformanceCalculator
{
    public static ExercisePerformanceSnapshot? Calculate(
        TrackingMode trackingMode,
        IEnumerable<ExercisePerformanceSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var history = samples.ToArray();
        if (history.Length == 0) return null;

        var latest = history
            .OrderByDescending(sample => sample.CompletedAt)
            .ThenByDescending(sample => sample.WorkoutId)
            .ThenByDescending(sample => sample.Order)
            .First();
        var usesPlates = latest.Set.PlateCount is not null;
        var latestSets = history
            .Where(sample => sample.WorkoutId == latest.WorkoutId)
            .Where(sample => (sample.Set.PlateCount is not null) == usesPlates)
            .Select(sample => sample.Set);
        var comparableSets = history
            .Where(sample => (sample.Set.PlateCount is not null) == usesPlates)
            .Select(sample => sample.Set);

        return new ExercisePerformanceSnapshot(
            latest.CompletedAt.ToUniversalTime(),
            Best(trackingMode, latestSets),
            Best(trackingMode, comparableSets));
    }

    public static ExercisePerformanceSet Best(
        TrackingMode trackingMode,
        IEnumerable<ExercisePerformanceSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var ordered = trackingMode switch
        {
            TrackingMode.Weighted => sets
                .OrderByDescending(set => set.PlateCount ?? int.MinValue)
                .ThenByDescending(set => set.WeightKg)
                .ThenByDescending(set => set.Reps),
            TrackingMode.Bodyweight => sets
                .OrderByDescending(set => set.Reps),
            TrackingMode.Assisted => sets
                .OrderBy(set => set.PlateCount ?? int.MaxValue)
                .ThenBy(set => set.AssistedKg)
                .ThenByDescending(set => set.Reps),
            _ => throw new ArgumentOutOfRangeException(nameof(trackingMode))
        };

        return ordered.First();
    }
}

public sealed record ExercisePerformanceSample(
    Guid WorkoutId,
    DateTimeOffset CompletedAt,
    Guid SetId,
    int Order,
    ExercisePerformanceSet Set);

public sealed record ExercisePerformanceSnapshot(
    DateTimeOffset LastPerformedAt,
    ExercisePerformanceSet LastBestSet,
    ExercisePerformanceSet AllTimeBest);
