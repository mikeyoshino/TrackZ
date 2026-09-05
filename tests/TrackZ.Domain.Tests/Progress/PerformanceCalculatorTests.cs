using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;

namespace TrackZ.Domain.Tests.Progress;

public sealed class PerformanceCalculatorTests
{
    [Fact]
    public void Weighted_prefers_higher_weight_then_more_reps()
    {
        var best = PerformanceCalculator.Best(
            TrackingMode.Weighted,
            [Set(70m, null, 8), Set(70m, null, 10), Set(67.5m, null, 12)]);

        Assert.Equal(new ExercisePerformanceSet(70m, null, 10), best);
    }

    [Fact]
    public void Bodyweight_prefers_more_reps()
    {
        var best = PerformanceCalculator.Best(
            TrackingMode.Bodyweight,
            [Set(null, null, 12), Set(null, null, 15), Set(null, null, 10)]);

        Assert.Equal(new ExercisePerformanceSet(null, null, 15), best);
    }

    [Fact]
    public void Assisted_prefers_lower_assistance_then_more_reps()
    {
        var best = PerformanceCalculator.Best(
            TrackingMode.Assisted,
            [Set(null, 30m, 10), Set(null, 25m, 8), Set(null, 25m, 9)]);

        Assert.Equal(new ExercisePerformanceSet(null, 25m, 9), best);
    }

    [Fact]
    public void Weighted_plate_count_prefers_more_plates_then_more_reps()
    {
        var best = PerformanceCalculator.Best(
            TrackingMode.Weighted,
            [new(null, null, 12, 6), new(null, null, 8, 7), new(null, null, 10, 7)]);

        Assert.Equal(new ExercisePerformanceSet(null, null, 10, 7), best);
    }

    [Fact]
    public void Assisted_plate_count_prefers_fewer_plates_then_more_reps()
    {
        var best = PerformanceCalculator.Best(
            TrackingMode.Assisted,
            [new(null, null, 10, 8), new(null, null, 8, 7), new(null, null, 9, 7)]);

        Assert.Equal(new ExercisePerformanceSet(null, null, 9, 7), best);
    }

    [Fact]
    public void Calculate_compares_only_the_latest_load_representation()
    {
        var older = Guid.NewGuid();
        var latest = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
        var snapshot = PerformanceCalculator.Calculate(
            TrackingMode.Weighted,
            [
                Sample(older, at, 0, 100m, null, 5),
                new ExercisePerformanceSample(
                    latest, at.AddDays(1), Guid.NewGuid(), 0,
                    new ExercisePerformanceSet(null, null, 10, 7))
            ]);

        Assert.Equal(new ExercisePerformanceSet(null, null, 10, 7), snapshot!.AllTimeBest);
    }

    [Fact]
    public void Calculate_uses_latest_session_for_last_and_all_sessions_for_personal_record()
    {
        var olderWorkoutId = Guid.Parse("10000000-0000-0000-0000-000000000000");
        var latestWorkoutId = Guid.Parse("20000000-0000-0000-0000-000000000000");
        var olderAt = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
        var latestAt = olderAt.AddDays(7);

        var snapshot = PerformanceCalculator.Calculate(
            TrackingMode.Weighted,
            [
                Sample(olderWorkoutId, olderAt, 0, 80m, null, 5),
                Sample(latestWorkoutId, latestAt, 0, 70m, null, 8),
                Sample(latestWorkoutId, latestAt, 1, 70m, null, 10)
            ]);

        Assert.NotNull(snapshot);
        Assert.Equal(latestAt, snapshot.LastPerformedAt);
        Assert.Equal(new ExercisePerformanceSet(70m, null, 10), snapshot.LastBestSet);
        Assert.Equal(new ExercisePerformanceSet(80m, null, 5), snapshot.AllTimeBest);
    }

    [Fact]
    public void Calculate_returns_null_when_completed_history_is_empty()
    {
        var snapshot = PerformanceCalculator.Calculate(
            TrackingMode.Bodyweight,
            []);

        Assert.Null(snapshot);
    }

    private static ExercisePerformanceSet Set(decimal? weightKg, decimal? assistedKg, int reps) =>
        new(weightKg, assistedKg, reps);

    private static ExercisePerformanceSample Sample(
        Guid workoutId,
        DateTimeOffset completedAt,
        int order,
        decimal? weightKg,
        decimal? assistedKg,
        int reps) =>
        new(workoutId, completedAt, Guid.NewGuid(), order, Set(weightKg, assistedKg, reps));
}
