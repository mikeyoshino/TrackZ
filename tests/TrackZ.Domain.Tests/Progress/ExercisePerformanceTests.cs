using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;

namespace TrackZ.Domain.Tests.Progress;

public sealed class ExercisePerformanceTests
{
    [Theory]
    [InlineData(TrackingMode.Weighted, 70d, null, 8)]
    [InlineData(TrackingMode.Bodyweight, null, null, 12)]
    [InlineData(TrackingMode.Assisted, null, 25d, 10)]
    public void Create_accepts_only_mode_coherent_performance_sets(
        TrackingMode trackingMode,
        double? weightKg,
        double? assistedKg,
        int reps)
    {
        var at = new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.FromHours(7));
        var set = new ExercisePerformanceSet(
            weightKg is null ? null : (decimal)weightKg.Value,
            assistedKg is null ? null : (decimal)assistedKg.Value,
            reps);

        var performance = ExercisePerformance.Create(
            Guid.NewGuid(), Guid.NewGuid(), trackingMode, at, set, set);

        Assert.Equal(trackingMode, performance.TrackingMode);
        Assert.Equal(TimeSpan.Zero, performance.LastPerformedAt!.Value.Offset);
        Assert.Equal(set.WeightKg, performance.LastBestWeightKg);
        Assert.Equal(set.AssistedKg, performance.AllTimeBestAssistedKg);
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, null, null, 8)]
    [InlineData(TrackingMode.Weighted, 70d, 10d, 8)]
    [InlineData(TrackingMode.Weighted, 0d, null, 8)]
    [InlineData(TrackingMode.Bodyweight, 1d, null, 8)]
    [InlineData(TrackingMode.Assisted, 1d, 10d, 8)]
    [InlineData(TrackingMode.Assisted, null, 0d, 8)]
    [InlineData(TrackingMode.Assisted, null, 10d, 0)]
    public void Create_rejects_incoherent_performance_sets(
        TrackingMode trackingMode,
        double? weightKg,
        double? assistedKg,
        int reps)
    {
        var set = new ExercisePerformanceSet(
            weightKg is null ? null : (decimal)weightKg.Value,
            assistedKg is null ? null : (decimal)assistedKg.Value,
            reps);

        Assert.Throws<ArgumentException>(() => ExercisePerformance.Create(
            Guid.NewGuid(), Guid.NewGuid(), trackingMode, DateTimeOffset.UtcNow, set, null));
    }

    [Fact]
    public void Create_requires_complete_last_and_all_time_projection_for_a_persisted_row()
    {
        var valid = new ExercisePerformanceSet(70m, null, 8);

        Assert.Throws<ArgumentException>(() => ExercisePerformance.Create(
            Guid.NewGuid(), Guid.NewGuid(), TrackingMode.Weighted, DateTimeOffset.UtcNow, valid, null));
        Assert.Throws<ArgumentException>(() => ExercisePerformance.Create(
            Guid.NewGuid(), Guid.NewGuid(), TrackingMode.Weighted, null, null, valid));
    }
}
