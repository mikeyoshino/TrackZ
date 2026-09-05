using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Progress;

/// <summary>
/// Durable, materialized per-user exercise performance projection. Workout processing owns recomputation.
/// </summary>
public sealed class ExercisePerformance
{
    private ExercisePerformance()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ExerciseDefinitionId { get; private set; }
    public TrackingMode TrackingMode { get; private set; }
    public DateTimeOffset? LastPerformedAt { get; private set; }
    public decimal? LastBestWeightKg { get; private set; }
    public decimal? LastBestAssistedKg { get; private set; }
    public int? LastBestPlateCount { get; private set; }
    public int? LastBestReps { get; private set; }
    public decimal? AllTimeBestWeightKg { get; private set; }
    public decimal? AllTimeBestAssistedKg { get; private set; }
    public int? AllTimeBestPlateCount { get; private set; }
    public int? AllTimeBestReps { get; private set; }

    public static ExercisePerformance Create(
        Guid userId,
        Guid exerciseDefinitionId,
        TrackingMode trackingMode,
        DateTimeOffset? lastPerformedAt,
        ExercisePerformanceSet? lastBestSet,
        ExercisePerformanceSet? allTimeBest)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(exerciseDefinitionId, Guid.Empty);
        if (!Enum.IsDefined(trackingMode)) throw new ArgumentOutOfRangeException(nameof(trackingMode));
        if (lastPerformedAt is null || lastBestSet is null || allTimeBest is null)
        {
            throw new ArgumentException("A persisted performance projection requires a timestamp, LAST set, and all-time-best set.");
        }

        var performance = new ExercisePerformance
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExerciseDefinitionId = exerciseDefinitionId
        };
        performance.Recalculate(trackingMode, lastPerformedAt.Value, lastBestSet, allTimeBest);
        return performance;
    }

    public void Recalculate(
        TrackingMode trackingMode,
        DateTimeOffset lastPerformedAt,
        ExercisePerformanceSet lastBestSet,
        ExercisePerformanceSet allTimeBest)
    {
        if (!Enum.IsDefined(trackingMode)) throw new ArgumentOutOfRangeException(nameof(trackingMode));
        if (lastPerformedAt == default) throw new ArgumentOutOfRangeException(nameof(lastPerformedAt));
        ArgumentNullException.ThrowIfNull(lastBestSet);
        ArgumentNullException.ThrowIfNull(allTimeBest);
        ValidateSet(trackingMode, lastBestSet, nameof(lastBestSet));
        ValidateSet(trackingMode, allTimeBest, nameof(allTimeBest));

        TrackingMode = trackingMode;
        LastPerformedAt = lastPerformedAt.ToUniversalTime();
        LastBestWeightKg = lastBestSet.WeightKg;
        LastBestAssistedKg = lastBestSet.AssistedKg;
        LastBestPlateCount = lastBestSet.PlateCount;
        LastBestReps = lastBestSet.Reps;
        AllTimeBestWeightKg = allTimeBest.WeightKg;
        AllTimeBestAssistedKg = allTimeBest.AssistedKg;
        AllTimeBestPlateCount = allTimeBest.PlateCount;
        AllTimeBestReps = allTimeBest.Reps;
    }

    private static void ValidateSet(TrackingMode trackingMode, ExercisePerformanceSet? set, string parameterName)
    {
        if (set is null) return;
        var valid = trackingMode switch
        {
            TrackingMode.Weighted => set.AssistedKg is null && set.Reps > 0
                && ((set.WeightKg is > 0m && set.PlateCount is null)
                    || (set.WeightKg is null && set.PlateCount is >= 1 and <= 999)),
            TrackingMode.Bodyweight => set.WeightKg is null && set.AssistedKg is null
                && set.PlateCount is null && set.Reps > 0,
            TrackingMode.Assisted => set.WeightKg is null && set.Reps > 0
                && ((set.AssistedKg is > 0m && set.PlateCount is null)
                    || (set.AssistedKg is null && set.PlateCount is >= 1 and <= 999)),
            _ => false
        };
        if (!valid) throw new ArgumentException("The performance set does not match the tracking mode.", parameterName);
    }
}

public sealed record ExercisePerformanceSet(
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    int? PlateCount = null);
