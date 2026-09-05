using System.Globalization;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Features.History;

public sealed record HistorySetMeasurement(
    decimal? WeightKg,
    decimal? AssistedKg,
    int Reps,
    int? PlateCount = null);

public sealed record HistoryMutationResult(LocalWorkout Workout, Guid OperationId);

public sealed class WorkoutHistoryCoordinator(
    ILocalWorkoutRepository workouts,
    IAccountSessionBoundary sessionBoundary,
    IClock clock,
    IWorkoutSyncTrigger? syncTrigger = null)
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public async Task<IReadOnlyList<LocalWorkout>> GetHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        IReadOnlyList<LocalWorkout>? history = null;
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            history = await workouts.GetHistoryAsync(token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
        return history ?? [];
    }

    public async Task<HistoryMutationResult> EditSetAsync(
        Guid workoutId,
        Guid workoutExerciseId,
        Guid setId,
        HistorySetMeasurement measurement,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(workoutId, workoutExerciseId, setId, operationId);
        ArgumentNullException.ThrowIfNull(measurement);
        return await MutateAsync(operationId, cancellationToken, async token =>
        {
            if (operationId is { } stableId
                && await workouts.GetOperationAsync(stableId, token) is { } existing)
                return await ReplayEditAsync(existing, workoutId, workoutExerciseId, setId, measurement, token);

            var previous = await RequiredWorkoutAsync(workoutId, token);
            EnsureNotDeleted(previous);
            var exercise = FindExercise(previous, workoutExerciseId);
            var set = FindSet(exercise, setId);
            ValidateMeasurement(exercise.TrackingMode, measurement);
            if (set.WeightKg == measurement.WeightKg
                && set.AssistedKg == measurement.AssistedKg
                && set.Reps == measurement.Reps)
            {
                return new HistoryMutationResult(previous, Guid.Empty);
            }
            var updatedAt = await NextMutationAtAsync(previous, token);
            var updatedSet = set with
            {
                WeightKg = measurement.WeightKg,
                AssistedKg = measurement.AssistedKg,
                PlateCount = measurement.PlateCount,
                Reps = measurement.Reps,
                UpdatedAt = updatedAt,
                Version = set.Version + 1
            };
            var updatedExercise = exercise with
            {
                Sets = exercise.Sets.Select(item => item.Id == setId ? updatedSet : item).ToArray(),
                Version = exercise.Version + 1
            };
            var updated = previous with
            {
                Exercises = previous.Exercises.Select(item =>
                    item.Id == workoutExerciseId ? updatedExercise : item).ToArray(),
                Version = previous.Version + 1
            };
            var id = operationId ?? Guid.NewGuid();
            var operation = OutboxOperation.Create(
                id,
                workoutId,
                OutboxOperationType.EditSet,
                new EditSetOutboxPayload(
                    workoutId, workoutExerciseId, setId,
                    DecimalText(measurement.WeightKg), DecimalText(measurement.AssistedKg),
                    measurement.Reps, updatedAt, measurement.PlateCount),
                previous.Version,
                updatedAt);
            await workouts.SaveHistoryMutationAndEnqueueAsync(previous, updated, operation, token);
            return new HistoryMutationResult(updated, id);
        });
    }

    public async Task<HistoryMutationResult> DeleteSetAsync(
        Guid workoutId,
        Guid workoutExerciseId,
        Guid setId,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(workoutId, workoutExerciseId, setId, operationId);
        return await MutateAsync(operationId, cancellationToken, async token =>
        {
            if (operationId is { } stableId
                && await workouts.GetOperationAsync(stableId, token) is { } existing)
                return await ReplayDeleteSetAsync(
                    existing, workoutId, workoutExerciseId, setId, token);

            var previous = await RequiredWorkoutAsync(workoutId, token);
            EnsureNotDeleted(previous);
            var exercise = FindExercise(previous, workoutExerciseId);
            var set = FindSet(exercise, setId);
            var deletedAt = await NextMutationAtAsync(previous, token);
            var remaining = exercise.Sets
                .Where(item => item.Id != setId && item.DeletedAt is null)
                .OrderBy(item => item.Order)
                .Select((item, order) => item.Order == order
                    ? item
                    : item with { Order = order, Version = item.Version + 1 })
                .ToDictionary(item => item.Id);
            var updatedSets = exercise.Sets.Select(item => item.Id == setId
                ? item with { DeletedAt = deletedAt, Version = item.Version + 1 }
                : item.DeletedAt is null ? remaining[item.Id] : item).ToArray();
            var updatedExercise = exercise with
            {
                Sets = updatedSets,
                Version = exercise.Version + 1
            };
            var updated = previous with
            {
                Exercises = previous.Exercises.Select(item =>
                    item.Id == workoutExerciseId ? updatedExercise : item).ToArray(),
                Version = previous.Version + 1
            };
            var id = operationId ?? Guid.NewGuid();
            var operation = OutboxOperation.Create(
                id,
                workoutId,
                OutboxOperationType.DeleteSet,
                new DeleteSetOutboxPayload(
                    workoutId, workoutExerciseId, setId, deletedAt),
                previous.Version,
                deletedAt);
            await workouts.SaveHistoryMutationAndEnqueueAsync(previous, updated, operation, token);
            return new HistoryMutationResult(updated, id);
        });
    }

    public async Task<HistoryMutationResult> DeleteWorkoutAsync(
        Guid workoutId,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (workoutId == Guid.Empty)
            throw new ArgumentException("Workout ID cannot be empty.", nameof(workoutId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        return await MutateAsync(operationId, cancellationToken, async token =>
        {
            if (operationId is { } stableId
                && await workouts.GetOperationAsync(stableId, token) is { } existing)
                return await ReplayDeleteWorkoutAsync(existing, workoutId, token);

            var previous = await RequiredWorkoutAsync(workoutId, token);
            EnsureNotDeleted(previous);
            var deletedAt = await NextMutationAtAsync(previous, token);
            var updated = previous with
            {
                DeletedAt = deletedAt,
                Version = previous.Version + 1
            };
            var id = operationId ?? Guid.NewGuid();
            var operation = OutboxOperation.Create(
                id,
                workoutId,
                OutboxOperationType.DeleteWorkout,
                new DeleteWorkoutOutboxPayload(workoutId, deletedAt),
                previous.Version,
                deletedAt);
            await workouts.SaveHistoryMutationAndEnqueueAsync(previous, updated, operation, token);
            return new HistoryMutationResult(updated, id);
        });
    }

    public async Task<HistoryMutationResult> DeleteWorkoutExerciseAsync(
        Guid workoutId,
        Guid workoutExerciseId,
        Guid? operationId = null,
        CancellationToken cancellationToken = default)
    {
        if (workoutId == Guid.Empty || workoutExerciseId == Guid.Empty)
            throw new ArgumentException("Stable workout and exercise IDs are required.");
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        return await MutateAsync(operationId, cancellationToken, async token =>
        {
            if (operationId is { } stableId
                && await workouts.GetOperationAsync(stableId, token) is { } existing)
                return await ReplayDeleteWorkoutExerciseAsync(
                    existing, workoutId, workoutExerciseId, token);

            var previous = await RequiredWorkoutAsync(workoutId, token);
            EnsureNotDeleted(previous);
            var exercise = FindExercise(previous, workoutExerciseId);
            var deletedAt = await NextMutationAtAsync(previous, token);
            var remaining = previous.Exercises
                .Where(item => item.Id != workoutExerciseId && item.DeletedAt is null)
                .OrderBy(item => item.Order)
                .Select((item, order) => item.Order == order
                    ? item
                    : item with { Order = order, Version = item.Version + 1 })
                .ToDictionary(item => item.Id);
            var updatedExercises = previous.Exercises.Select(item => item.Id == exercise.Id
                ? item with { DeletedAt = deletedAt, Version = item.Version + 1 }
                : item.DeletedAt is null ? remaining[item.Id] : item).ToArray();
            var updated = previous with
            {
                Exercises = updatedExercises,
                Version = previous.Version + 1
            };
            var id = operationId ?? Guid.NewGuid();
            var operation = OutboxOperation.Create(
                id,
                workoutId,
                OutboxOperationType.DeleteWorkoutExercise,
                new DeleteWorkoutExerciseOutboxPayload(
                    workoutId, workoutExerciseId, deletedAt),
                previous.Version,
                deletedAt);
            await workouts.SaveHistoryMutationAndEnqueueAsync(previous, updated, operation, token);
            return new HistoryMutationResult(updated, id);
        });
    }

    public async Task<LocalWorkout> UndoAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionBoundary.Capture();
            LocalWorkout? restored = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                var latest = await workouts.GetLatestOperationCreatedAtAsync(token);
                var now = Utc(clock.UtcNow);
                var neutralizedAt = latest is { } value && now <= value ? value.AddTicks(1) : now;
                restored = await workouts.UndoHistoryMutationAsync(
                    operationId, neutralizedAt, token);
            }, cancellationToken);
            EnsureCurrent(committed, generation, cancellationToken);
            syncTrigger?.NotifyMutation();
            return restored ?? throw new InvalidOperationException("The history operation was not undone.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<HistoryMutationResult> MutateAsync(
        Guid? operationId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<HistoryMutationResult>> mutation)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionBoundary.Capture();
            HistoryMutationResult? result = null;
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                result = await mutation(token);
            }, cancellationToken);
            EnsureCurrent(committed, generation, cancellationToken);
            if (result?.OperationId != Guid.Empty) syncTrigger?.NotifyMutation();
            return result ?? throw new InvalidOperationException("The history mutation did not commit.");
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<HistoryMutationResult> ReplayEditAsync(
        OutboxOperation operation,
        Guid workoutId,
        Guid exerciseId,
        Guid setId,
        HistorySetMeasurement measurement,
        CancellationToken cancellationToken)
    {
        var payload = operation.DeserializePayload<EditSetOutboxPayload>();
        if (operation.Type != OutboxOperationType.EditSet
            || operation.EntityId != workoutId
            || operation.NeutralizedAt is not null
            || payload.WorkoutId != workoutId
            || payload.WorkoutExerciseId != exerciseId
            || payload.SetId != setId
            || payload.WeightKg != DecimalText(measurement.WeightKg)
            || payload.AssistedKg != DecimalText(measurement.AssistedKg)
            || payload.Reps != measurement.Reps)
            throw new InvalidDataException(
                "The operation identifier is already bound to a different history edit.");
        return new HistoryMutationResult(
            await RequiredWorkoutAsync(workoutId, cancellationToken), operation.OperationId);
    }

    private async Task<HistoryMutationResult> ReplayDeleteSetAsync(
        OutboxOperation operation,
        Guid workoutId,
        Guid exerciseId,
        Guid setId,
        CancellationToken cancellationToken)
    {
        var payload = operation.DeserializePayload<DeleteSetOutboxPayload>();
        if (operation.Type != OutboxOperationType.DeleteSet
            || operation.EntityId != workoutId
            || operation.NeutralizedAt is not null
            || payload.WorkoutId != workoutId
            || payload.WorkoutExerciseId != exerciseId
            || payload.SetId != setId)
            throw new InvalidDataException(
                "The operation identifier is already bound to a different set deletion.");
        return new HistoryMutationResult(
            await RequiredWorkoutAsync(workoutId, cancellationToken), operation.OperationId);
    }

    private async Task<HistoryMutationResult> ReplayDeleteWorkoutAsync(
        OutboxOperation operation,
        Guid workoutId,
        CancellationToken cancellationToken)
    {
        var payload = operation.DeserializePayload<DeleteWorkoutOutboxPayload>();
        if (operation.Type != OutboxOperationType.DeleteWorkout
            || operation.EntityId != workoutId
            || operation.NeutralizedAt is not null
            || payload.WorkoutId != workoutId)
            throw new InvalidDataException(
                "The operation identifier is already bound to a different workout deletion.");
        return new HistoryMutationResult(
            await RequiredWorkoutAsync(workoutId, cancellationToken), operation.OperationId);
    }

    private async Task<HistoryMutationResult> ReplayDeleteWorkoutExerciseAsync(
        OutboxOperation operation,
        Guid workoutId,
        Guid workoutExerciseId,
        CancellationToken cancellationToken)
    {
        var payload = operation.DeserializePayload<DeleteWorkoutExerciseOutboxPayload>();
        if (operation.Type != OutboxOperationType.DeleteWorkoutExercise
            || operation.EntityId != workoutId
            || operation.NeutralizedAt is not null
            || payload.WorkoutId != workoutId
            || payload.WorkoutExerciseId != workoutExerciseId)
            throw new InvalidDataException(
                "The operation identifier is already bound to a different exercise deletion.");
        return new HistoryMutationResult(
            await RequiredWorkoutAsync(workoutId, cancellationToken), operation.OperationId);
    }

    private async Task<LocalWorkout> RequiredWorkoutAsync(
        Guid workoutId,
        CancellationToken cancellationToken) =>
        await workouts.GetHistoryWorkoutAsync(workoutId, cancellationToken)
        ?? throw new InvalidOperationException("The completed workout is unavailable.");

    private async Task<DateTimeOffset> NextMutationAtAsync(
        LocalWorkout workout,
        CancellationToken cancellationToken)
    {
        var latest = LastMutationAt(workout);
        var latestOperation = await workouts.GetLatestOperationCreatedAtAsync(cancellationToken);
        if (latestOperation > latest) latest = latestOperation.Value;
        var requested = Utc(clock.UtcNow);
        return requested <= latest ? latest.AddTicks(1) : requested;
    }

    private static LocalWorkoutExercise FindExercise(LocalWorkout workout, Guid exerciseId) =>
        workout.Exercises.SingleOrDefault(item => item.Id == exerciseId && item.DeletedAt is null)
        ?? throw new ArgumentException("The workout exercise is unavailable.", nameof(exerciseId));

    private static LocalSet FindSet(LocalWorkoutExercise exercise, Guid setId) =>
        exercise.Sets.SingleOrDefault(item => item.Id == setId && item.DeletedAt is null)
        ?? throw new ArgumentException("The set is unavailable.", nameof(setId));

    private static void EnsureNotDeleted(LocalWorkout workout)
    {
        if (workout.DeletedAt is not null)
            throw new InvalidOperationException("A deleted workout cannot be changed again.");
    }

    private static void ValidateMeasurement(TrackingMode mode, HistorySetMeasurement measurement)
    {
        var valid = measurement.Reps is >= 1 and <= 999 && mode switch
        {
            TrackingMode.Weighted => measurement.AssistedKg is null
                && ((Kilograms(measurement.WeightKg) && measurement.PlateCount is null)
                    || (measurement.WeightKg is null && Plates(measurement.PlateCount))),
            TrackingMode.Bodyweight => measurement.WeightKg is null
                && measurement.AssistedKg is null && measurement.PlateCount is null,
            TrackingMode.Assisted => measurement.WeightKg is null
                && ((Kilograms(measurement.AssistedKg) && measurement.PlateCount is null)
                    || (measurement.AssistedKg is null && Plates(measurement.PlateCount))),
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The history set measurement is invalid.", nameof(measurement));
    }

    private static bool Kilograms(decimal? value) => value is { } kilograms
        && kilograms is >= 0.001m and <= 99999.999m
        && ((decimal.GetBits(kilograms)[3] >> 16) & 0xff) <= 3;

    private static bool Plates(int? value) => value is >= 1 and <= 999;

    private static void ValidateIds(
        Guid workoutId,
        Guid exerciseId,
        Guid setId,
        Guid? operationId)
    {
        if (workoutId == Guid.Empty || exerciseId == Guid.Empty || setId == Guid.Empty)
            throw new ArgumentException("Stable workout, exercise, and set IDs are required.");
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
    }

    private void EnsureCurrent(
        bool committed,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        if (committed && !sessionBoundary.IsCancellationRequested(generation)) return;
        throw new OperationCanceledException("The account session changed.", cancellationToken);
    }

    private static DateTimeOffset LastMutationAt(LocalWorkout workout)
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

    private static DateTimeOffset Utc(DateTimeOffset value) => value.ToUniversalTime();
    private static string? DecimalText(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);
}
