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
    Guid OperationId)
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
            Guid.NewGuid())
    {
    }
}
