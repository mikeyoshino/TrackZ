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
    public int? LastBestReps { get; private set; }
    public decimal? AllTimeBestWeightKg { get; private set; }
    public decimal? AllTimeBestAssistedKg { get; private set; }
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

        ValidateSet(trackingMode, lastBestSet, nameof(lastBestSet));
        ValidateSet(trackingMode, allTimeBest, nameof(allTimeBest));

        return new ExercisePerformance
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExerciseDefinitionId = exerciseDefinitionId,
            TrackingMode = trackingMode,
            LastPerformedAt = lastPerformedAt?.ToUniversalTime(),
            LastBestWeightKg = lastBestSet?.WeightKg,
            LastBestAssistedKg = lastBestSet?.AssistedKg,
            LastBestReps = lastBestSet?.Reps,
            AllTimeBestWeightKg = allTimeBest?.WeightKg,
            AllTimeBestAssistedKg = allTimeBest?.AssistedKg,
            AllTimeBestReps = allTimeBest?.Reps
        };
    }

    private static void ValidateSet(TrackingMode trackingMode, ExercisePerformanceSet? set, string parameterName)
    {
        if (set is null) return;
        var valid = trackingMode switch
        {
            TrackingMode.Weighted => set.WeightKg is > 0m && set.AssistedKg is null && set.Reps > 0,
            TrackingMode.Bodyweight => set.WeightKg is null && set.AssistedKg is null && set.Reps > 0,
            TrackingMode.Assisted => set.WeightKg is null && set.AssistedKg is > 0m && set.Reps > 0,
            _ => false
        };
        if (!valid) throw new ArgumentException("The performance set does not match the tracking mode.", parameterName);
    }
}

public sealed record ExercisePerformanceSet(decimal? WeightKg, decimal? AssistedKg, int Reps);
