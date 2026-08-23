using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Features.Workout;

public sealed class ActiveWorkoutCoordinator(
    ILocalWorkoutRepository workouts,
    IAccountSessionBoundary sessionBoundary,
    IClock clock,
    IWorkoutSyncTrigger? syncTrigger = null)
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public Task<LocalWorkout> StartAsync(
        IReadOnlyList<Guid> exerciseIds,
        CancellationToken cancellationToken = default) =>
        StartAsync(
            exerciseIds.Select(id => new WorkoutExerciseSelection(id, TrackingMode.Weighted)).ToArray(),
            cancellationToken: cancellationToken);

    public async Task<LocalWorkout> StartAsync(
        IReadOnlyList<WorkoutExerciseSelection> selections,
        Guid? workoutId = null,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0
            || selections.Any(item => item.ExerciseDefinitionId == Guid.Empty || !Enum.IsDefined(item.TrackingMode))
            || selections.Select(item => item.ExerciseDefinitionId).Distinct().Count() != selections.Count)
            throw new ArgumentException("A workout needs unique valid exercise selections.", nameof(selections));
        if (workoutId == Guid.Empty) throw new ArgumentException("Workout ID cannot be empty.", nameof(workoutId));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

        var generation = sessionBoundary.Capture();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            LocalWorkout? created = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                if (await workouts.GetActiveAsync(token) is { } active)
                {
                    if (workoutId is null || operationId is null)
                        throw new InvalidOperationException("An active workout already exists.");
                    created = await ReconcileStartReplayAsync(
                        active, selections, workoutId.Value, operationId.Value, token);
                    return;
                }
                if (operationId is { } requestedOperation
                    && await workouts.GetOperationAsync(requestedOperation, token) is not null)
                    throw new InvalidDataException(
                        "The start operation exists without its active workout.");

                var requestedAt = Utc(clock.UtcNow);
                var latestOperationAt = await workouts.GetLatestOperationCreatedAtAsync(token);
                var startedAt = AfterCausalWatermark(requestedAt, latestOperationAt);
                var id = workoutId ?? Guid.NewGuid();
                var exercises = selections.Select((selection, order) => new LocalWorkoutExercise(
                    Guid.NewGuid(),
                    id,
                    selection.ExerciseDefinitionId,
                    selection.TrackingMode,
                    order,
                    null,
                    1,
                    0,
                    [])).ToArray();
                created = new LocalWorkout(
                    id,
                    LocalWorkoutStatus.Active,
                    startedAt,
                    null,
                    null,
                    selections.Count,
                    0,
                    exercises);
                var payload = new StartWorkoutOutboxPayload(
                    id,
                    startedAt,
                    exercises.Select(item => new StartWorkoutExercisePayload(
                        item.Id,
                        item.ExerciseDefinitionId,
                        (int)item.TrackingMode,
                        item.Order)).ToArray());
                var operation = OutboxOperation.Create(
                    operationId ?? Guid.NewGuid(),
                    id,
                    OutboxOperationType.StartWorkout,
                    payload,
                    created.BaseVersion,
                    startedAt);
                await workouts.SaveWorkoutAndEnqueueAsync(created, operation, token);
            }, cancellationToken);

            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return created ?? throw new InvalidOperationException("The active workout was not created.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<LocalSet> SaveSetAsync(
        Guid exerciseDefinitionId,
        LocalSet set,
        CancellationToken cancellationToken = default)
    {
        if (exerciseDefinitionId == Guid.Empty)
            throw new ArgumentException("Exercise ID cannot be empty.", nameof(exerciseDefinitionId));
        ArgumentNullException.ThrowIfNull(set);
        if (set.Id == Guid.Empty || set.OperationId == Guid.Empty)
            throw new ArgumentException("Set and operation IDs must be stable non-empty values.", nameof(set));
        if (set.Effort is not null)
            throw new ArgumentException(
                "A new set must be saved before effort is recorded.", nameof(set));

        var generation = sessionBoundary.Capture();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            LocalSet? saved = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                var active = await workouts.GetActiveAsync(token)
                    ?? throw new InvalidOperationException("No active workout exists.");
                var exercise = active.Exercises.SingleOrDefault(item =>
                    item.DeletedAt is null && item.ExerciseDefinitionId == exerciseDefinitionId)
                    ?? throw new ArgumentException("The exercise is not in the active workout.", nameof(exerciseDefinitionId));
                var existing = exercise.Sets.SingleOrDefault(item => item.Id == set.Id);
                if (existing is not null)
                {
                    if (existing.OperationId != set.OperationId
                        || existing.WeightKg != set.WeightKg
                        || existing.AssistedKg != set.AssistedKg
                        || existing.Reps != set.Reps
                        || set.CompletedAt != default && existing.CompletedAt != Utc(set.CompletedAt))
                        throw new InvalidDataException("The set ID is already bound to a different measurement.");
                    saved = existing with { OperationId = set.OperationId };
                    var prior = await workouts.GetOperationAsync(set.OperationId, token)
                        ?? throw new InvalidDataException("The saved set has no durable outbox operation.");
                    var priorPayload = prior.DeserializePayload<SaveSetOutboxPayload>();
                    if (prior.EntityId != active.Id
                        || prior.Type != OutboxOperationType.SaveSet
                        || priorPayload.WorkoutId != active.Id
                        || priorPayload.WorkoutExerciseId != exercise.Id
                        || priorPayload.SetId != existing.Id
                        || priorPayload.Order != existing.Order
                        || priorPayload.WeightKg != DecimalText(existing.WeightKg)
                        || priorPayload.AssistedKg != DecimalText(existing.AssistedKg)
                        || priorPayload.Reps != existing.Reps
                        || priorPayload.CompletedAt != existing.CompletedAt)
                        throw new InvalidDataException("The saved set and outbox operation contracts diverge.");
                    return;
                }
                else
                {
                    var lastMutation = LastAggregateMutationAt(active);
                    var latestOperationAt = await workouts.GetLatestOperationCreatedAtAsync(token);
                    if (latestOperationAt is { } latest && latest > lastMutation)
                        lastMutation = latest;
                    var requestedAt = set.CompletedAt == default ? Utc(clock.UtcNow) : Utc(set.CompletedAt);
                    var completedAt = AfterCausalWatermark(requestedAt, lastMutation);
                    saved = set with
                    {
                        WorkoutExerciseId = exercise.Id,
                        Order = exercise.Sets.Count(item => item.DeletedAt is null),
                        CompletedAt = completedAt,
                        UpdatedAt = null,
                        DeletedAt = null,
                        Version = 1,
                        BaseVersion = 0
                    };
                }

                var sets = existing is null
                    ? exercise.Sets.Append(saved).ToArray()
                    : exercise.Sets.ToArray();
                var updatedExercise = exercise with
                {
                    Sets = sets,
                    Version = existing is null ? exercise.Version + 1 : exercise.Version
                };
                var graph = active with
                {
                    Exercises = active.Exercises
                        .Select(item => item.Id == exercise.Id ? updatedExercise : item)
                        .ToArray(),
                    Version = existing is null ? active.Version + 1 : active.Version
                };
                var durableSet = saved;
                var operation = OutboxOperation.Create(
                    set.OperationId,
                    graph.Id,
                    OutboxOperationType.SaveSet,
                    new SaveSetOutboxPayload(
                        graph.Id,
                        exercise.Id,
                        durableSet.Id,
                        durableSet.Order,
                        DecimalText(durableSet.WeightKg),
                        DecimalText(durableSet.AssistedKg),
                        durableSet.Reps,
                        durableSet.CompletedAt),
                    active.Version,
                    durableSet.CompletedAt);
                await workouts.SaveWorkoutAndEnqueueAsync(graph, operation, token);
            }, cancellationToken);

            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return saved ?? throw new InvalidOperationException("The set was not saved.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<LocalWorkout> FinishAsync(
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

        var generation = sessionBoundary.Capture();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            LocalWorkout? completed = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                if (operationId is { } stableId
                    && await workouts.GetOperationAsync(stableId, token) is { } existing)
                {
                    if (existing.Type != OutboxOperationType.CompleteWorkout
                        || existing.NeutralizedAt is not null)
                        throw new InvalidDataException(
                            "The operation identifier is already bound to another workout intent.");
                    var payload = existing.DeserializePayload<CompleteWorkoutOutboxPayload>();
                    var durable = await workouts.GetHistoryWorkoutAsync(payload.WorkoutId, token)
                        ?? throw new InvalidDataException(
                            "The completed workout is missing for its durable operation.");
                    if (existing.EntityId != durable.Id
                        || payload.WorkoutId != durable.Id
                        || payload.CompletedAt != durable.CompletedAt)
                        throw new InvalidDataException(
                            "The completed workout and outbox operation contracts diverge.");
                    completed = durable;
                    return;
                }
                var active = await workouts.GetActiveAsync(token)
                    ?? throw new InvalidOperationException("No active workout exists.");
                var activeExercises = active.Exercises.Where(item => item.DeletedAt is null).ToArray();
                if (activeExercises.Length == 0
                    || activeExercises.Any(exercise => exercise.Sets.All(set => set.DeletedAt is not null)))
                    throw new InvalidOperationException(
                        "A workout requires at least one completed set for every active exercise.");

                var latest = LastAggregateMutationAt(active);
                var latestOperationAt = await workouts.GetLatestOperationCreatedAtAsync(token);
                if (latestOperationAt is { } operationAt && operationAt > latest) latest = operationAt;
                var completedAt = AfterCausalWatermark(Utc(clock.UtcNow), latest);
                completed = active with
                {
                    Status = LocalWorkoutStatus.Completed,
                    CompletedAt = completedAt,
                    Version = active.Version + 1
                };
                var operation = OutboxOperation.Create(
                    operationId ?? Guid.NewGuid(),
                    active.Id,
                    OutboxOperationType.CompleteWorkout,
                    new CompleteWorkoutOutboxPayload(active.Id, completedAt),
                    active.Version,
                    completedAt);
                await workouts.SaveHistoryMutationAndEnqueueAsync(active, completed, operation, token);
            }, cancellationToken);

            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return completed ?? throw new InvalidOperationException("The workout was not completed.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<LocalWorkoutExercise> AddExerciseAsync(
        WorkoutExerciseSelection selection,
        Guid? workoutExerciseId = null,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (selection.ExerciseDefinitionId == Guid.Empty || !Enum.IsDefined(selection.TrackingMode))
            throw new ArgumentException("A valid exercise selection is required.", nameof(selection));
        if (workoutExerciseId == Guid.Empty)
            throw new ArgumentException("Workout exercise ID cannot be empty.", nameof(workoutExerciseId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

        var generation = sessionBoundary.Capture();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            LocalWorkoutExercise? added = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                var active = await workouts.GetActiveAsync(token)
                    ?? throw new InvalidOperationException("No active workout exists.");
                if (operationId is { } stableOperationId
                    && await workouts.GetOperationAsync(stableOperationId, token) is { } replay)
                {
                    var replayPayload = replay.DeserializePayload<AddExerciseOutboxPayload>();
                    if (replay.Type != OutboxOperationType.AddExercise
                        || replay.EntityId != active.Id
                        || replayPayload.ExerciseDefinitionId != selection.ExerciseDefinitionId
                        || replayPayload.TrackingMode != (int)selection.TrackingMode
                        || workoutExerciseId is { } stableExerciseId
                        && replayPayload.WorkoutExerciseId != stableExerciseId)
                        throw new InvalidDataException(
                            "The operation identifier is bound to another exercise addition.");
                    added = active.Exercises.Single(item =>
                        item.Id == replayPayload.WorkoutExerciseId && item.DeletedAt is null);
                    return;
                }
                if (active.Exercises.Any(item =>
                        item.DeletedAt is null
                        && item.ExerciseDefinitionId == selection.ExerciseDefinitionId))
                    throw new InvalidOperationException("The exercise is already in the active workout.");

                var addedAt = await NextMutationAtAsync(active, token);
                var exercise = new LocalWorkoutExercise(
                    workoutExerciseId ?? Guid.NewGuid(),
                    active.Id,
                    selection.ExerciseDefinitionId,
                    selection.TrackingMode,
                    active.Exercises.Count(item => item.DeletedAt is null),
                    null,
                    1,
                    0,
                    []);
                var updated = active with
                {
                    Exercises = [.. active.Exercises, exercise],
                    Version = active.Version + 1
                };
                var operation = OutboxOperation.Create(
                    operationId ?? Guid.NewGuid(),
                    active.Id,
                    OutboxOperationType.AddExercise,
                    new AddExerciseOutboxPayload(
                        active.Id,
                        exercise.Id,
                        exercise.ExerciseDefinitionId,
                        (int)exercise.TrackingMode,
                        exercise.Order,
                        addedAt),
                    active.Version,
                    addedAt);
                await workouts.SaveWorkoutAndEnqueueAsync(updated, operation, token);
                added = exercise;
            }, cancellationToken);
            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return added ?? throw new InvalidOperationException("The exercise was not added.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<LocalWorkout> RemoveExerciseAsync(
        Guid workoutExerciseId,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (workoutExerciseId == Guid.Empty)
            throw new ArgumentException("Workout exercise ID cannot be empty.", nameof(workoutExerciseId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

        var generation = sessionBoundary.Capture();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            LocalWorkout? result = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                var active = await workouts.GetActiveAsync(token)
                    ?? throw new InvalidOperationException("No active workout exists.");
                if (operationId is { } stableOperationId
                    && await workouts.GetOperationAsync(stableOperationId, token) is { } replay)
                {
                    var payload = replay.DeserializePayload<RemoveExerciseOutboxPayload>();
                    if (replay.Type != OutboxOperationType.RemoveExercise
                        || replay.EntityId != active.Id
                        || payload.WorkoutExerciseId != workoutExerciseId)
                        throw new InvalidDataException(
                            "The operation identifier is bound to another exercise removal.");
                    result = active;
                    return;
                }
                var target = active.Exercises.SingleOrDefault(item =>
                    item.Id == workoutExerciseId && item.DeletedAt is null)
                    ?? throw new ArgumentException(
                        "The exercise is not in the active workout.", nameof(workoutExerciseId));
                if (active.Exercises.Count(item => item.DeletedAt is null) == 1)
                    throw new InvalidOperationException("An active workout needs at least one exercise.");
                var deletedAt = await NextMutationAtAsync(active, token);
                var activeRemaining = active.Exercises
                    .Where(item => item.Id != target.Id && item.DeletedAt is null)
                    .OrderBy(item => item.Order)
                    .Select((item, order) => item.Order == order
                        ? item
                        : item with { Order = order, Version = item.Version + 1 })
                    .ToDictionary(item => item.Id);
                result = active with
                {
                    Exercises = active.Exercises.Select(item => item.Id == target.Id
                        ? item with { DeletedAt = deletedAt, Version = item.Version + 1 }
                        : item.DeletedAt is null ? activeRemaining[item.Id] : item).ToArray(),
                    Version = active.Version + 1
                };
                var operation = OutboxOperation.Create(
                    operationId ?? Guid.NewGuid(),
                    active.Id,
                    OutboxOperationType.RemoveExercise,
                    new RemoveExerciseOutboxPayload(active.Id, target.Id, deletedAt),
                    active.Version,
                    deletedAt);
                await workouts.SaveWorkoutAndEnqueueAsync(result, operation, token);
            }, cancellationToken);
            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return result ?? throw new InvalidOperationException("The exercise was not removed.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<LocalWorkout> ReorderExercisesAsync(
        IReadOnlyList<Guid> orderedWorkoutExerciseIds,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedWorkoutExerciseIds);
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

        var generation = sessionBoundary.Capture();
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            LocalWorkout? result = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                var active = await workouts.GetActiveAsync(token)
                    ?? throw new InvalidOperationException("No active workout exists.");
                if (operationId is { } stableOperationId
                    && await workouts.GetOperationAsync(stableOperationId, token) is { } replay)
                {
                    var payload = replay.DeserializePayload<ReorderExercisesOutboxPayload>();
                    if (replay.Type != OutboxOperationType.ReorderExercises
                        || replay.EntityId != active.Id
                        || !payload.WorkoutExerciseIds.SequenceEqual(orderedWorkoutExerciseIds))
                        throw new InvalidDataException(
                            "The operation identifier is bound to another exercise order.");
                    result = active;
                    return;
                }
                var current = active.Exercises.Where(item => item.DeletedAt is null)
                    .OrderBy(item => item.Order).ToArray();
                if (orderedWorkoutExerciseIds.Count != current.Length
                    || orderedWorkoutExerciseIds.Any(id => id == Guid.Empty)
                    || orderedWorkoutExerciseIds.Distinct().Count() != current.Length
                    || orderedWorkoutExerciseIds.Any(id => current.All(item => item.Id != id)))
                    throw new ArgumentException(
                        "The order must contain every active exercise exactly once.",
                        nameof(orderedWorkoutExerciseIds));
                if (current.Select(item => item.Id).SequenceEqual(orderedWorkoutExerciseIds))
                    throw new InvalidOperationException("The exercise order has not changed.");

                var reorderedAt = await NextMutationAtAsync(active, token);
                var orders = orderedWorkoutExerciseIds
                    .Select((id, order) => (id, order)).ToDictionary(item => item.id, item => item.order);
                result = active with
                {
                    Exercises = active.Exercises.Select(item => item.DeletedAt is null
                        ? item with { Order = orders[item.Id], Version = item.Version + 1 }
                        : item).ToArray(),
                    Version = active.Version + 1
                };
                var operation = OutboxOperation.Create(
                    operationId ?? Guid.NewGuid(),
                    active.Id,
                    OutboxOperationType.ReorderExercises,
                    new ReorderExercisesOutboxPayload(
                        active.Id, orderedWorkoutExerciseIds.ToArray(), reorderedAt),
                    active.Version,
                    reorderedAt);
                await workouts.SaveWorkoutAndEnqueueAsync(result, operation, token);
            }, cancellationToken);
            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return result ?? throw new InvalidOperationException("The exercises were not reordered.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<LocalWorkout?> RestoreActiveAsync(CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        LocalWorkout? active = null;
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            active = await workouts.GetActiveAsync(token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
        return active;
    }

    public Task ClearPrivateDataAsync(CancellationToken cancellationToken = default) =>
        workouts.ClearPrivateDataAsync(cancellationToken);

    private void EnsureCurrent(
        bool committed,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        if (committed && !sessionBoundary.IsCancellationRequested(generation)) return;
        throw new OperationCanceledException("The account session changed.", cancellationToken);
    }

    private static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
    private static DateTimeOffset AfterCausalWatermark(
        DateTimeOffset requestedAt,
        DateTimeOffset? watermark) =>
        watermark is { } latest && requestedAt <= latest
            ? latest.AddTicks(1)
            : requestedAt;
    private static string? DecimalText(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset LastAggregateMutationAt(LocalWorkout workout)
    {
        var latest = workout.StartedAt;
        Include(workout.CompletedAt);
        Include(workout.DeletedAt);
        foreach (var exercise in workout.Exercises)
        {
            Include(exercise.DeletedAt);
            foreach (var set in exercise.Sets)
            {
                Include(set.CompletedAt);
                Include(set.UpdatedAt);
                Include(set.DeletedAt);
            }
        }
        return latest;

        void Include(DateTimeOffset? timestamp)
        {
            if (timestamp > latest) latest = timestamp.Value;
        }
    }

    private async Task<DateTimeOffset> NextMutationAtAsync(
        LocalWorkout workout,
        CancellationToken cancellationToken)
    {
        var latest = LastAggregateMutationAt(workout);
        var latestOperationAt = await workouts.GetLatestOperationCreatedAtAsync(cancellationToken);
        if (latestOperationAt is { } operationAt && operationAt > latest) latest = operationAt;
        return AfterCausalWatermark(Utc(clock.UtcNow), latest);
    }

    private async Task<LocalWorkout> ReconcileStartReplayAsync(
        LocalWorkout active,
        IReadOnlyList<WorkoutExerciseSelection> selections,
        Guid workoutId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await workouts.GetOperationAsync(operationId, cancellationToken)
            ?? throw new InvalidDataException("The active workout belongs to a different start operation.");
        if (active.Id != workoutId
            || operation.EntityId != workoutId
            || operation.Type != OutboxOperationType.StartWorkout
            || operation.BaseVersion != 0)
            throw new InvalidDataException("The active workout start contract does not match the replay.");

        var payload = operation.DeserializePayload<StartWorkoutOutboxPayload>();
        if (payload.WorkoutId != workoutId
            || payload.StartedAt != active.StartedAt
            || payload.Exercises.Count != selections.Count)
            throw new InvalidDataException("The active workout start payload does not match the replay.");
        for (var order = 0; order < selections.Count; order++)
        {
            var requested = selections[order];
            var stored = payload.Exercises[order];
            var persisted = active.Exercises.SingleOrDefault(item => item.Id == stored.WorkoutExerciseId);
            if (stored.Order != order
                || stored.ExerciseDefinitionId != requested.ExerciseDefinitionId
                || stored.TrackingMode != (int)requested.TrackingMode
                || persisted is null
                || persisted.ExerciseDefinitionId != stored.ExerciseDefinitionId
                || persisted.TrackingMode != requested.TrackingMode)
                throw new InvalidDataException("The active workout exercise contract does not match the replay.");
        }
        return active;
    }
}
