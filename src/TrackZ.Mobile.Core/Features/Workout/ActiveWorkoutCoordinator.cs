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
                if (await workouts.GetActiveAsync(token) is not null)
                    throw new InvalidOperationException("An active workout already exists.");

                var startedAt = Utc(clock.UtcNow);
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
                    1,
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
                    if (existing.WeightKg != set.WeightKg
                        || existing.AssistedKg != set.AssistedKg
                        || existing.Reps != set.Reps
                        || set.CompletedAt != default && existing.CompletedAt != Utc(set.CompletedAt))
                        throw new InvalidDataException("The set ID is already bound to a different measurement.");
                    saved = existing with { OperationId = set.OperationId };
                }
                else
                {
                    var lastMutation = exercise.Sets
                        .Select(item => item.CompletedAt)
                        .Append(active.StartedAt)
                        .Max();
                    var requestedAt = set.CompletedAt == default ? Utc(clock.UtcNow) : Utc(set.CompletedAt);
                    var completedAt = requestedAt <= lastMutation ? lastMutation.AddTicks(1) : requestedAt;
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
                    graph.BaseVersion,
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
    private static string? DecimalText(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
}
