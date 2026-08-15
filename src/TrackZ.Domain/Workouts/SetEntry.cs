namespace TrackZ.Domain.Workouts;

public sealed class SetEntry
{
    private SetEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkoutExerciseId { get; private set; }

    public int Order { get; private set; }

    public decimal? WeightKg { get; private set; }

    public decimal? AssistedKg { get; private set; }

    public int Reps { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public long Version { get; private set; }

    public SetMeasurement Measurement => new(WeightKg, AssistedKg, Reps);

    internal DateTimeOffset LastMutationAt
    {
        get
        {
            var latest = CompletedAt;
            if (UpdatedAt is { } updatedAt && updatedAt > latest)
            {
                latest = updatedAt;
            }

            if (DeletedAt is { } deletedAt && deletedAt > latest)
            {
                latest = deletedAt;
            }

            return latest;
        }
    }

    internal static SetEntry Create(
        Guid id,
        Guid workoutExerciseId,
        int order,
        SetMeasurement measurement,
        DateTimeOffset completedAt)
    {
        return new SetEntry
        {
            Id = id,
            WorkoutExerciseId = workoutExerciseId,
            Order = order,
            WeightKg = measurement.WeightKg,
            AssistedKg = measurement.AssistedKg,
            Reps = measurement.Reps,
            CompletedAt = completedAt,
            Version = 1
        };
    }

    internal bool Edit(SetMeasurement measurement, DateTimeOffset updatedAt)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted set cannot be edited.");
        }

        if (updatedAt < CompletedAt || UpdatedAt is { } priorUpdate && updatedAt < priorUpdate)
        {
            throw new ArgumentException(
                "The update timestamp cannot precede an earlier set mutation.",
                nameof(updatedAt));
        }

        if (Measurement == measurement)
        {
            return false;
        }

        WeightKg = measurement.WeightKg;
        AssistedKg = measurement.AssistedKg;
        Reps = measurement.Reps;
        UpdatedAt = updatedAt;
        Version++;
        return true;
    }

    internal bool Delete(DateTimeOffset deletedAt)
    {
        if (IsDeleted)
        {
            return false;
        }

        if (deletedAt < LastMutationAt)
        {
            throw new ArgumentException(
                "The deletion timestamp cannot precede an earlier set mutation.",
                nameof(deletedAt));
        }

        DeletedAt = deletedAt;
        Version++;
        return true;
    }

    internal void ChangeOrder(int order)
    {
        if (Order == order)
        {
            return;
        }

        Order = order;
        Version++;
    }
}
