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
    IClock clock)
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
            return saved ?? throw new InvalidOperationException("The set was not saved.");
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
