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
            .First();
        var latestSets = history
            .Where(sample => sample.WorkoutId == latest.WorkoutId)
            .Select(sample => sample.Set);

        return new ExercisePerformanceSnapshot(
            latest.CompletedAt.ToUniversalTime(),
            Best(trackingMode, latestSets),
            Best(trackingMode, history.Select(sample => sample.Set)));
    }

    public static ExercisePerformanceSet Best(
        TrackingMode trackingMode,
        IEnumerable<ExercisePerformanceSet> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var ordered = trackingMode switch
        {
            TrackingMode.Weighted => sets
                .OrderByDescending(set => set.WeightKg)
                .ThenByDescending(set => set.Reps),
            TrackingMode.Bodyweight => sets
                .OrderByDescending(set => set.Reps),
            TrackingMode.Assisted => sets
                .OrderBy(set => set.AssistedKg)
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
