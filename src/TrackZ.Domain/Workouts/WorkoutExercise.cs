using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Workouts;

public sealed class WorkoutExercise
{
    private readonly List<SetEntry> _sets = [];

    private WorkoutExercise()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkoutSessionId { get; private set; }

    public Guid ExerciseDefinitionId { get; private set; }

    public TrackingMode TrackingMode { get; private set; }

    public int Order { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public long Version { get; private set; }

    public IReadOnlyList<SetEntry> Sets => _sets
        .Where(set => !set.IsDeleted)
        .OrderBy(set => set.Order)
        .ToList()
        .AsReadOnly();

    public IReadOnlyList<SetEntry> SetEntries => _sets.AsReadOnly();

    internal DateTimeOffset? LastMutationAt
    {
        get
        {
            var latest = DeletedAt;
            foreach (var set in _sets)
            {
                if (latest is null || set.LastMutationAt > latest)
                {
                    latest = set.LastMutationAt;
                }
            }

            return latest;
        }
    }

    internal static WorkoutExercise Create(
        Guid id,
        Guid workoutSessionId,
        Guid exerciseDefinitionId,
        TrackingMode trackingMode,
        int order)
    {
        return new WorkoutExercise
        {
            Id = id,
            WorkoutSessionId = workoutSessionId,
            ExerciseDefinitionId = exerciseDefinitionId,
            TrackingMode = trackingMode,
            Order = order,
            Version = 1
        };
    }

    internal SetEntry AddSet(Guid setId, SetMeasurement measurement, DateTimeOffset completedAt)
    {
        EnsureNotDeleted();
        ValidateMeasurement(measurement);
        var set = SetEntry.Create(setId, Id, TrackingMode, Sets.Count, measurement, completedAt);
        _sets.Add(set);
        Version++;
        return set;
    }

    internal bool EditSet(Guid setId, SetMeasurement measurement, DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        ValidateMeasurement(measurement);
        var set = FindSet(setId);
        var changed = set.Edit(measurement, updatedAt);
        if (changed)
        {
            Version++;
        }

        return changed;
    }

    internal bool RecordSetEffort(Guid setId, SetEffortRating effort, DateTimeOffset recordedAt)
    {
        EnsureNotDeleted();
        var set = FindSet(setId);
        if (!set.RecordEffort(effort, recordedAt))
        {
            return false;
        }

        Version++;
        return true;
    }

    internal bool DeleteSet(Guid setId, DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();
        var set = FindSet(setId);
        if (!set.Delete(deletedAt))
        {
            return false;
        }

        ReindexSets();
        Version++;
        return true;
    }

    internal bool Delete(DateTimeOffset deletedAt)
    {
        if (IsDeleted)
        {
            return false;
        }

        if (_sets.Any(set => deletedAt < set.LastMutationAt))
        {
            throw new ArgumentException(
                "The deletion timestamp cannot precede a child set mutation.",
                nameof(deletedAt));
        }

        var activeSets = Sets;
        foreach (var set in activeSets)
        {
            set.Delete(deletedAt);
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

    private SetEntry FindSet(Guid setId)
    {
        var set = _sets.SingleOrDefault(candidate => candidate.Id == setId);
        return set ?? throw new ArgumentException("The set does not belong to this workout exercise.", nameof(setId));
    }

    private void ReindexSets()
    {
        var activeSets = Sets;
        for (var index = 0; index < activeSets.Count; index++)
        {
            activeSets[index].ChangeOrder(index);
        }
    }

    private void ValidateMeasurement(SetMeasurement measurement)
    {
        if (measurement is null)
        {
            throw new WorkoutRuleException(
                WorkoutRuleViolation.InvalidSetValue,
                "A set measurement is required.");
        }

        var validReps = measurement.Reps is >= 1 and <= 999;
        var validForMode = TrackingMode switch
        {
            TrackingMode.Weighted =>
                ((IsRepresentableKilograms(measurement.WeightKg) && measurement.PlateCount is null)
                    || (measurement.WeightKg is null && IsValidPlateCount(measurement.PlateCount)))
                && measurement.AssistedKg is null,
            TrackingMode.Bodyweight => measurement.WeightKg is null
                && measurement.AssistedKg is null
                && measurement.PlateCount is null,
            TrackingMode.Assisted => measurement.WeightKg is null
                && ((IsRepresentableKilograms(measurement.AssistedKg) && measurement.PlateCount is null)
                    || (measurement.AssistedKg is null && IsValidPlateCount(measurement.PlateCount))),
            _ => false
        };

        if (!validReps || !validForMode)
        {
            throw new WorkoutRuleException(
                WorkoutRuleViolation.InvalidSetValue,
                "The set measurement is invalid for the exercise tracking mode.");
        }
    }

    private static bool IsRepresentableKilograms(decimal? value)
    {
        return value is { } kilograms
            && kilograms is >= SetMeasurement.MinimumKilograms and <= SetMeasurement.MaximumKilograms
            && DecimalScale(kilograms) <= SetMeasurement.MaximumKilogramScale;
    }

    private static bool IsValidPlateCount(int? value) =>
        value is >= SetMeasurement.MinimumPlateCount and <= SetMeasurement.MaximumPlateCount;

    private static int DecimalScale(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0xff;

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted workout exercise cannot be changed.");
        }
    }
}
