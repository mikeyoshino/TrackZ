using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Data.Models;

public sealed record LocalSet(
    Guid Id,
    Guid WorkoutExerciseId,
    int Order,
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    long BaseVersion,
    Guid OperationId,
    SetEffortRating? Effort = null,
    int? PlateCount = null,
    int? EffortScore = null,
    bool? IsWarmup = null,
    bool? HasPain = null)
{
    public LocalSet(decimal? weightKg, decimal? assistedKg, int reps)
        : this(
            Guid.NewGuid(),
            Guid.Empty,
            0,
            weightKg,
            assistedKg,
            reps,
            DateTimeOffset.MinValue,
            null,
            null,
            1,
            0,
            Guid.NewGuid(),
            null)
    {
    }

    public LocalSet(decimal? weightKg, decimal? assistedKg, int reps, int? plateCount)
        : this(
            Guid.NewGuid(), Guid.Empty, 0, weightKg, assistedKg, reps,
            DateTimeOffset.MinValue, null, null, 1, 0, Guid.NewGuid(), null, plateCount)
    {
    }
}
