namespace TrackZ.Domain.Workouts;

using TrackZ.Domain.Exercises;

public sealed class SetEntry
{
    private SetEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkoutExerciseId { get; private set; }

    public TrackingMode TrackingMode { get; private set; }

    public int Order { get; private set; }

    public decimal? WeightKg { get; private set; }

    public decimal? AssistedKg { get; private set; }

    public int? PlateCount { get; private set; }

    public int Reps { get; private set; }

    public SetEffortRating? Effort { get; private set; }

    public DateTimeOffset CompletedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public long Version { get; private set; }

    public SetMeasurement Measurement => new(WeightKg, AssistedKg, Reps, PlateCount);

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
        TrackingMode trackingMode,
        int order,
        SetMeasurement measurement,
        DateTimeOffset completedAt)
    {
        return new SetEntry
        {
            Id = id,
            WorkoutExerciseId = workoutExerciseId,
            TrackingMode = trackingMode,
            Order = order,
            WeightKg = measurement.WeightKg,
            AssistedKg = measurement.AssistedKg,
            PlateCount = measurement.PlateCount,
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
        PlateCount = measurement.PlateCount;
        Reps = measurement.Reps;
        UpdatedAt = updatedAt;
        Version++;
        return true;
    }

    internal bool RecordEffort(SetEffortRating effort, DateTimeOffset recordedAt)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted set cannot record effort.");
        }

        if (!Enum.IsDefined(effort))
        {
            throw new ArgumentOutOfRangeException(nameof(effort));
        }

        if (Effort == effort)
        {
            return false;
        }

        if (recordedAt < LastMutationAt)
        {
            throw new ArgumentException(
                "The effort timestamp cannot precede an earlier set mutation.",
                nameof(recordedAt));
        }

        Effort = effort;
        UpdatedAt = recordedAt;
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
