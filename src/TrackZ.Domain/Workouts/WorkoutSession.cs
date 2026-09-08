using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Workouts;

public sealed class WorkoutSession
{
    private readonly List<WorkoutExercise> _exercises = [];

    private WorkoutSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public WorkoutStatus Status { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public long Version { get; private set; }

    public IReadOnlyList<WorkoutExercise> Exercises => _exercises
        .Where(exercise => !exercise.IsDeleted)
        .OrderBy(exercise => exercise.Order)
        .ToList()
        .AsReadOnly();

    public IReadOnlyList<WorkoutExercise> ExerciseEntries => _exercises.AsReadOnly();

    public static WorkoutSession Start(Guid ownerId, Guid workoutId, DateTimeOffset startedAt)
    {
        EnsureNotEmpty(ownerId, nameof(ownerId));
        EnsureNotEmpty(workoutId, nameof(workoutId));

        return new WorkoutSession
        {
            Id = workoutId,
            OwnerId = ownerId,
            Status = WorkoutStatus.Active,
            StartedAt = NormalizeTimestamp(startedAt, nameof(startedAt)),
            Version = 0
        };
    }

    public void AddExercise(
        Guid workoutExerciseId,
        Guid exerciseDefinitionId,
        TrackingMode trackingMode,
        int order)
    {
        EnsureActive();
        EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
        EnsureNotEmpty(exerciseDefinitionId, nameof(exerciseDefinitionId));
        if (!Enum.IsDefined(trackingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(trackingMode));
        }

        if (_exercises.Any(exercise => exercise.Id == workoutExerciseId))
        {
            throw new ArgumentException("The workout exercise identifier is already in use.", nameof(workoutExerciseId));
        }

        if (Exercises.Any(exercise => exercise.ExerciseDefinitionId == exerciseDefinitionId))
        {
            throw new ArgumentException("The exercise is already in this workout.", nameof(exerciseDefinitionId));
        }

        if (order < 0 || order > Exercises.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(order));
        }

        foreach (var exercise in Exercises.Where(exercise => exercise.Order >= order))
        {
            exercise.ChangeOrder(exercise.Order + 1);
        }

        _exercises.Add(WorkoutExercise.Create(
            workoutExerciseId,
            Id,
            exerciseDefinitionId,
            trackingMode,
            order));
        Version++;
    }

    public void ReorderExercises(IReadOnlyList<Guid> orderedWorkoutExerciseIds)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(orderedWorkoutExerciseIds);

        var activeExercises = Exercises;
        if (orderedWorkoutExerciseIds.Count != activeExercises.Count
            || orderedWorkoutExerciseIds.Any(id => id == Guid.Empty)
            || orderedWorkoutExerciseIds.Distinct().Count() != orderedWorkoutExerciseIds.Count
            || orderedWorkoutExerciseIds.Any(id => activeExercises.All(exercise => exercise.Id != id)))
        {
            throw new ArgumentException(
                "Exercise order must contain every active workout exercise exactly once.",
                nameof(orderedWorkoutExerciseIds));
        }

        if (activeExercises.Select(exercise => exercise.Id).SequenceEqual(orderedWorkoutExerciseIds))
        {
            return;
        }

        for (var index = 0; index < orderedWorkoutExerciseIds.Count; index++)
        {
            activeExercises.Single(exercise => exercise.Id == orderedWorkoutExerciseIds[index]).ChangeOrder(index);
        }

        Version++;
    }

    public void CompleteSet(
        Guid workoutExerciseId,
        Guid setId,
        SetMeasurement measurement,
        DateTimeOffset completedAt,
        int? effortScore = null,
        bool? isWarmup = null,
        bool? hasPain = null)
    {
        EnsureActive();
        EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
        EnsureNotEmpty(setId, nameof(setId));
        var exercise = FindActiveExercise(workoutExerciseId);
        if (_exercises.SelectMany(item => item.SetEntries).Any(set => set.Id == setId))
        {
            throw new ArgumentException("The set identifier is already in use.", nameof(setId));
        }

        var normalizedCompletedAt = NormalizeTimestamp(completedAt, nameof(completedAt));
        EnsureNotBeforeStart(normalizedCompletedAt, nameof(completedAt));
        exercise.AddSet(setId, measurement, normalizedCompletedAt, effortScore, isWarmup, hasPain);
        Version++;
    }

    public void EditSet(
        Guid workoutExerciseId,
        Guid setId,
        SetMeasurement measurement,
        DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
        EnsureNotEmpty(setId, nameof(setId));
        var exercise = FindActiveExercise(workoutExerciseId);
        var normalizedUpdatedAt = NormalizeTimestamp(updatedAt, nameof(updatedAt));
        EnsureNotBeforeStart(normalizedUpdatedAt, nameof(updatedAt));
        EnsureNotBeforeCompletion(normalizedUpdatedAt, nameof(updatedAt));
        if (exercise.EditSet(setId, measurement, normalizedUpdatedAt))
        {
            Version++;
        }
    }

    public void EditSetCoaching(Guid workoutExerciseId, Guid setId, int? effortScore,
        bool? isWarmup, bool? hasPain, DateTimeOffset updatedAt)
    {
        EnsureNotDeleted();
        var exercise = FindActiveExercise(workoutExerciseId);
        var normalized = NormalizeTimestamp(updatedAt, nameof(updatedAt));
        if (exercise.EditSetCoaching(setId, effortScore, isWarmup, hasPain, normalized)) Version++;
    }

    public void RecordSetEffort(
        Guid workoutExerciseId,
        Guid setId,
        SetEffortRating effort,
        DateTimeOffset recordedAt)
    {
        EnsureActive();
        EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
        EnsureNotEmpty(setId, nameof(setId));
        if (!Enum.IsDefined(effort))
        {
            throw new ArgumentOutOfRangeException(nameof(effort));
        }

        var exercise = FindActiveExercise(workoutExerciseId);
        var normalized = NormalizeTimestamp(recordedAt, nameof(recordedAt));
        EnsureNotBeforeStart(normalized, nameof(recordedAt));
        EnsureNotBeforeCompletion(normalized, nameof(recordedAt));
        if (exercise.RecordSetEffort(setId, effort, normalized))
        {
            Version++;
        }
    }

    public void DeleteSet(Guid workoutExerciseId, Guid setId, DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();
        EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
        EnsureNotEmpty(setId, nameof(setId));
        var exercise = FindActiveExercise(workoutExerciseId);
        var normalizedDeletedAt = NormalizeTimestamp(deletedAt, nameof(deletedAt));
        EnsureNotBeforeStart(normalizedDeletedAt, nameof(deletedAt));
        EnsureNotBeforeCompletion(normalizedDeletedAt, nameof(deletedAt));
        if (exercise.DeleteSet(setId, normalizedDeletedAt))
        {
            Version++;
        }
    }

    public void DeleteExercise(Guid workoutExerciseId, DateTimeOffset deletedAt)
    {
        EnsureNotDeleted();
        EnsureNotEmpty(workoutExerciseId, nameof(workoutExerciseId));
        var exercise = FindExercise(workoutExerciseId);
        if (exercise.IsDeleted)
        {
            return;
        }

        var normalizedDeletedAt = NormalizeTimestamp(deletedAt, nameof(deletedAt));
        EnsureNotBeforeStart(normalizedDeletedAt, nameof(deletedAt));
        EnsureNotBeforeCompletion(normalizedDeletedAt, nameof(deletedAt));
        if (!exercise.Delete(normalizedDeletedAt))
        {
            return;
        }

        ReindexExercises();
        Version++;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureActive();
        var activeExercises = Exercises;
        if (activeExercises.Count == 0 || activeExercises.Any(exercise => exercise.Sets.Count == 0))
        {
            throw new InvalidOperationException("A workout requires at least one completed set for every active exercise.");
        }

        var normalizedCompletedAt = NormalizeTimestamp(completedAt, nameof(completedAt));
        EnsureNotBeforeStart(normalizedCompletedAt, nameof(completedAt));
        if (LatestDescendantMutationAt() is { } latestMutationAt
            && normalizedCompletedAt < latestMutationAt)
        {
            throw new ArgumentException(
                "Workout completion cannot precede a descendant mutation.",
                nameof(completedAt));
        }

        Status = WorkoutStatus.Completed;
        CompletedAt = normalizedCompletedAt;
        Version++;
    }

    public void Delete(DateTimeOffset deletedAt)
    {
        if (IsDeleted)
        {
            return;
        }

        var normalizedDeletedAt = NormalizeTimestamp(deletedAt, nameof(deletedAt));
        EnsureNotBeforeStart(normalizedDeletedAt, nameof(deletedAt));
        EnsureNotBeforeCompletion(normalizedDeletedAt, nameof(deletedAt));
        if (LatestDescendantMutationAt() is { } latestMutationAt
            && normalizedDeletedAt < latestMutationAt)
        {
            throw new ArgumentException(
                "Workout deletion cannot precede a descendant mutation.",
                nameof(deletedAt));
        }

        DeletedAt = normalizedDeletedAt;
        Version++;
    }

    private WorkoutExercise FindActiveExercise(Guid workoutExerciseId)
    {
        var exercise = FindExercise(workoutExerciseId);
        if (exercise.IsDeleted)
        {
            throw new ArgumentException("The workout exercise has been deleted.", nameof(workoutExerciseId));
        }

        return exercise;
    }

    private WorkoutExercise FindExercise(Guid workoutExerciseId)
    {
        var exercise = _exercises.SingleOrDefault(candidate => candidate.Id == workoutExerciseId);
        return exercise ?? throw new ArgumentException(
            "The workout exercise does not belong to this workout.",
            nameof(workoutExerciseId));
    }

    private void ReindexExercises()
    {
        var activeExercises = Exercises;
        for (var index = 0; index < activeExercises.Count; index++)
        {
            activeExercises[index].ChangeOrder(index);
        }
    }

    private void EnsureActive()
    {
        EnsureNotDeleted();
        if (Status == WorkoutStatus.Completed)
        {
            throw new WorkoutRuleException(
                WorkoutRuleViolation.WorkoutAlreadyCompleted,
                "The workout has already been completed.");
        }

        if (Status != WorkoutStatus.Active)
        {
            throw new InvalidOperationException("The workout is not active.");
        }
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted workout cannot be changed.");
        }
    }

    private void EnsureNotBeforeStart(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp < StartedAt)
        {
            throw new ArgumentException("The timestamp cannot precede workout start.", parameterName);
        }
    }

    private void EnsureNotBeforeCompletion(DateTimeOffset timestamp, string parameterName)
    {
        if (CompletedAt is { } completedAt && timestamp < completedAt)
        {
            throw new ArgumentException(
                "The timestamp cannot precede workout completion.",
                parameterName);
        }
    }

    private DateTimeOffset? LatestDescendantMutationAt()
    {
        DateTimeOffset? latest = null;
        foreach (var exercise in _exercises)
        {
            if (exercise.LastMutationAt is { } mutationAt
                && (latest is null || mutationAt > latest))
            {
                latest = mutationAt;
            }
        }

        return latest;
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp == default)
        {
            throw new ArgumentException("A timestamp is required.", parameterName);
        }

        return timestamp.ToUniversalTime();
    }

    private static void EnsureNotEmpty(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An identifier is required.", parameterName);
        }
    }
}
