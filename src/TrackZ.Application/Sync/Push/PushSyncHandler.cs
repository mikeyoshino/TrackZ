using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Sync;
using TrackZ.Domain.Workouts;
using TrackZ.Application.Progress.ReconcileUserProgress;

namespace TrackZ.Application.Sync.Push;

public sealed class PushSyncHandler(
    ISyncPushStore store,
    ISender sender,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<PushSyncCommand, SyncPushResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SyncPushResponse> Handle(PushSyncCommand request, CancellationToken cancellationToken)
    {
        if (request.Operations is null) return new SyncPushResponse([]);
        var results = new List<SyncOperationResultDto>(request.Operations.Count);
        foreach (var operation in request.Operations)
        {
            results.Add(operation is null
                ? Rejected(Guid.Empty)
                : await ProcessAsync(operation, cancellationToken));
        }

        return new SyncPushResponse(results);
    }

    private async Task<SyncOperationResultDto> ProcessAsync(
        SyncOperationDto operation,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId == Guid.Empty) return Rejected(Guid.Empty);
        var fingerprint = Fingerprint(operation);
        var commitStarted = false;
        try
        {
            await using var transaction = await store.BeginSyncTransactionAsync(cancellationToken);
            await store.AcquireOperationLockAsync(currentUser.UserId, operation.OperationId, cancellationToken);
            var processed = await store.FindProcessedOperationAsync(
                currentUser.UserId, operation.OperationId, cancellationToken);
            if (processed is not null)
            {
                var replay = processed.RequestFingerprint == fingerprint
                    ? JsonSerializer.Deserialize<SyncOperationResultDto>(processed.ResultJson, JsonOptions)
                    : Rejected(operation.OperationId);
                commitStarted = true;
                await transaction.CommitAsync(cancellationToken);
                return replay ?? Rejected(operation.OperationId);
            }

            var result = await DispatchAsync(operation, cancellationToken);
            if (result.Mutation?.Workout is { } workout)
            {
                var payloadJson = JsonSerializer.Serialize(ToDto(workout), JsonOptions);
                store.AddSyncChange(SyncChange.Create(
                    currentUser.UserId,
                    operation.OperationId,
                    workout.Id,
                    workout.Version,
                    workout.IsDeleted,
                    payloadJson,
                    timeProvider.GetUtcNow()));
            }
            var publicResult = result.Result;
            var serializedResult = JsonSerializer.Serialize(publicResult, JsonOptions);
            store.AddProcessedOperation(ProcessedClientOperation.Create(
                currentUser.UserId,
                operation.OperationId,
                fingerprint,
                serializedResult,
                timeProvider.GetUtcNow()));
            await store.SaveSyncChangesAsync(cancellationToken);
            if (result.Mutation?.Workout is { } changedWorkout
                && RequiresPerformanceRecomputation(operation.Action))
            {
                await sender.Send(new ReconcileUserProgressCommand(
                    currentUser.UserId,
                    changedWorkout.Id,
                    changedWorkout.ExerciseEntries
                        .Select(exercise => exercise.ExerciseDefinitionId)
                        .Distinct()
                        .ToArray()), cancellationToken);
            }
            commitStarted = true;
            await transaction.CommitAsync(cancellationToken);
            return publicResult;
        }
        catch (Exception exception) when (
            !commitStarted
            && exception is not OperationCanceledException
            && store.IsTransient(exception))
        {
            return new SyncOperationResultDto(
                operation.OperationId,
                SyncOperationStatus.Retryable,
                null,
                BusinessErrorCode.InternalServerError);
        }
        finally
        {
            store.ClearSyncTracking();
        }
    }

    private async Task<DispatchResult> DispatchAsync(
        SyncOperationDto operation,
        CancellationToken cancellationToken)
    {
        if (operation.BaseVersion is not long baseVersion
            || baseVersion < 0
            || operation.Payload.ValueKind != JsonValueKind.Object
            || !string.Equals(operation.EntityType, "Workout", StringComparison.Ordinal))
        {
            return new DispatchResult(Rejected(operation.OperationId), null);
        }

        try
        {
            if (!HasActionContract(operation.Action, operation.Payload))
            {
                return new DispatchResult(Rejected(operation.OperationId), null);
            }

            var mutation = operation.Action switch
            {
                "StartWorkout" => await sender.Send(new StartWorkoutSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<StartWorkoutSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "SaveSet" => await sender.Send(new SaveSetSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<SaveSetSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "CompleteWorkout" => await sender.Send(new CompleteWorkoutSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<CompleteWorkoutSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "EditSet" => await sender.Send(new EditSetSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<EditSetSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "DeleteSet" => await sender.Send(new DeleteSetSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<DeleteSetSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "DeleteWorkout" => await sender.Send(new DeleteWorkoutSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<DeleteWorkoutSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "AddExercise" => await sender.Send(new AddExerciseSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<AddExerciseSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "RemoveExercise" => await sender.Send(new RemoveExerciseSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<RemoveExerciseSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "ReorderExercises" => await sender.Send(new ReorderExercisesSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<ReorderExercisesSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                "DeleteWorkoutExercise" => await sender.Send(new DeleteWorkoutExerciseSyncCommand(
                    baseVersion,
                    operation.Payload.Deserialize<DeleteWorkoutExerciseSyncPayload>(JsonOptions)
                        ?? throw new JsonException()), cancellationToken),
                _ => SyncMutationResult.Rejected()
            };
            return new DispatchResult(new SyncOperationResultDto(
                operation.OperationId,
                mutation.Status,
                mutation.ServerVersion,
                mutation.ErrorCode), mutation);
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException or FormatException or OverflowException)
        {
            return new DispatchResult(Rejected(operation.OperationId), null);
        }
    }

    private static SyncWorkoutDto ToDto(WorkoutSession workout) => new(
        workout.Id,
        (int)workout.Status,
        workout.StartedAt,
        workout.CompletedAt,
        workout.DeletedAt,
        workout.Version,
        workout.ExerciseEntries.OrderBy(exercise => exercise.Order).Select(exercise =>
            new SyncWorkoutExerciseDto(
                exercise.Id,
                exercise.ExerciseDefinitionId,
                (int)exercise.TrackingMode,
                exercise.Order,
                exercise.DeletedAt,
                exercise.Version,
                exercise.SetEntries.OrderBy(set => set.Order).Select(set => new SyncSetDto(
                    set.Id,
                    set.Order,
                    set.WeightKg?.ToString(CultureInfo.InvariantCulture),
                    set.AssistedKg?.ToString(CultureInfo.InvariantCulture),
                    set.Reps,
                    set.CompletedAt,
                    set.UpdatedAt,
                    set.DeletedAt,
                    set.Version)).ToArray())).ToArray());

    private sealed record DispatchResult(SyncOperationResultDto Result, SyncMutationResult? Mutation);

    private static SyncOperationResultDto Rejected(Guid operationId) => new(
        operationId,
        SyncOperationStatus.Rejected,
        null,
        BusinessErrorCode.InvalidRequest);

    private static bool HasStartWorkoutContract(JsonElement payload)
    {
        if (!HasProperties(payload, "workoutId", "startedAt", "exercises")
            || payload.GetProperty("exercises").ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return payload.GetProperty("exercises").EnumerateArray().All(exercise =>
            HasProperties(
                exercise,
                "workoutExerciseId",
                "exerciseDefinitionId",
                "trackingMode",
                "order"));
    }

    private static bool HasSaveSetContract(JsonElement payload) => HasProperties(
        payload,
        "workoutId",
        "workoutExerciseId",
        "setId",
        "order",
        "weightKg",
        "assistedKg",
        "reps",
        "completedAt");

    private static bool HasActionContract(string action, JsonElement payload) => action switch
    {
        "StartWorkout" => HasStartWorkoutContract(payload),
        "SaveSet" => HasSaveSetContract(payload),
        "CompleteWorkout" => HasProperties(payload, "workoutId", "completedAt"),
        "EditSet" => HasProperties(
            payload, "workoutId", "workoutExerciseId", "setId",
            "weightKg", "assistedKg", "reps", "updatedAt"),
        "DeleteSet" => HasProperties(
            payload, "workoutId", "workoutExerciseId", "setId", "deletedAt"),
        "DeleteWorkout" => HasProperties(payload, "workoutId", "deletedAt"),
        "AddExercise" => HasProperties(
            payload, "workoutId", "workoutExerciseId", "exerciseDefinitionId",
            "trackingMode", "order", "addedAt"),
        "RemoveExercise" => HasProperties(
            payload, "workoutId", "workoutExerciseId", "deletedAt"),
        "ReorderExercises" => HasProperties(
            payload, "workoutId", "workoutExerciseIds", "reorderedAt"),
        "DeleteWorkoutExercise" => HasProperties(
            payload, "workoutId", "workoutExerciseId", "deletedAt"),
        _ => false
    };

    private static bool RequiresPerformanceRecomputation(string action) => action is
        "CompleteWorkout" or "EditSet" or "DeleteSet" or "DeleteWorkout"
        or "RemoveExercise" or "DeleteWorkoutExercise";

    private static bool HasProperties(JsonElement element, params string[] propertyNames) =>
        element.ValueKind == JsonValueKind.Object
        && propertyNames.All(name => element.TryGetProperty(name, out _));

    private static string Fingerprint(SyncOperationDto operation)
    {
        var immutableRequest = JsonSerializer.Serialize(new
        {
            operation.OperationId,
            operation.EntityType,
            operation.Action,
            PayloadKind = operation.Payload.ValueKind,
            Payload = operation.Payload.ValueKind == JsonValueKind.Undefined
                ? null
                : operation.Payload.GetRawText(),
            operation.BaseVersion
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(immutableRequest)));
    }
}

internal sealed class StartWorkoutSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<StartWorkoutSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        StartWorkoutSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (request.BaseVersion < 0
            || payload.WorkoutId == Guid.Empty
            || payload.StartedAt == default
            || payload.Exercises is null
            || payload.Exercises.Count == 0
            || !HasExactContiguousOrders(payload.Exercises)
            || payload.Exercises.Any(exercise =>
                exercise.WorkoutExerciseId == Guid.Empty
                || exercise.ExerciseDefinitionId == Guid.Empty
                || !Enum.IsDefined((TrackingMode)exercise.TrackingMode))
            || payload.Exercises.Select(exercise => exercise.WorkoutExerciseId).Distinct().Count()
                != payload.Exercises.Count
            || payload.Exercises.Select(exercise => exercise.ExerciseDefinitionId).Distinct().Count()
                != payload.Exercises.Count)
        {
            return SyncMutationResult.Rejected();
        }

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var existing = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (existing is null
            && await store.FindWorkoutOwnerAsync(payload.WorkoutId, cancellationToken) is not null)
            return SyncMutationResult.Rejected();

        var exerciseIds = payload.Exercises.Select(exercise => exercise.ExerciseDefinitionId).ToArray();
        var authoritativeModes = await store.GetAvailableExerciseTrackingModesAsync(
            currentUser.UserId, exerciseIds, cancellationToken);
        if (authoritativeModes.Count != exerciseIds.Length
            || payload.Exercises.Any(exercise =>
                !authoritativeModes.TryGetValue(exercise.ExerciseDefinitionId, out var mode)
                || (int)mode != exercise.TrackingMode))
        {
            return SyncMutationResult.Rejected();
        }

        if (existing is not null)
        {
            if (existing.Version != request.BaseVersion)
                return SyncMutationResult.Conflict(existing.Version);
            if (existing.Status != WorkoutStatus.Active || existing.IsDeleted)
                return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutAlreadyCompleted);

            var missing = new List<StartWorkoutExerciseSyncPayload>();
            foreach (var requested in payload.Exercises.OrderBy(exercise => exercise.Order))
            {
                var byIdentity = existing.ExerciseEntries.SingleOrDefault(exercise =>
                    exercise.Id == requested.WorkoutExerciseId);
                if (byIdentity is not null)
                {
                    if (byIdentity.IsDeleted
                        || byIdentity.ExerciseDefinitionId != requested.ExerciseDefinitionId
                        || (int)byIdentity.TrackingMode != requested.TrackingMode)
                        return SyncMutationResult.Rejected();
                    continue;
                }
                if (existing.Exercises.Any(exercise =>
                        exercise.ExerciseDefinitionId == requested.ExerciseDefinitionId))
                    return SyncMutationResult.Rejected();
                missing.Add(requested);
            }
            if (missing.Count > 0
                && !await store.AreWorkoutExerciseIdentifiersAvailableAsync(
                    missing.Select(exercise => exercise.WorkoutExerciseId).ToArray(),
                    cancellationToken))
                return SyncMutationResult.Rejected();

            try
            {
                foreach (var exercise in missing.OrderBy(exercise => exercise.Order))
                    existing.AddExercise(
                        exercise.WorkoutExerciseId,
                        exercise.ExerciseDefinitionId,
                        authoritativeModes[exercise.ExerciseDefinitionId],
                        exercise.Order!.Value);
                var requestedOrder = payload.Exercises.OrderBy(exercise => exercise.Order)
                    .Select(exercise => exercise.WorkoutExerciseId).ToList();
                requestedOrder.AddRange(existing.Exercises
                    .Where(exercise => !requestedOrder.Contains(exercise.Id))
                    .OrderBy(exercise => exercise.Order)
                    .Select(exercise => exercise.Id));
                existing.ReorderExercises(requestedOrder);
                return SyncMutationResult.Applied(existing);
            }
            catch (Exception exception) when (
                exception is WorkoutRuleException or ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException(
                    "A validated start-workout merge failed atomically.", exception);
            }
        }

        if (request.BaseVersion != 0)
            return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        var workoutExerciseIds = payload.Exercises.Select(exercise => exercise.WorkoutExerciseId).ToArray();
        if (!await store.AreWorkoutExerciseIdentifiersAvailableAsync(
                workoutExerciseIds, cancellationToken))
            return SyncMutationResult.Rejected();

        try
        {
            var workout = WorkoutSession.Start(currentUser.UserId, payload.WorkoutId, payload.StartedAt);
            foreach (var exercise in payload.Exercises.OrderBy(exercise => exercise.Order.GetValueOrDefault()))
            {
                if (exercise.Order is not int order
                    || order < 0
                    || !Enum.IsDefined((TrackingMode)exercise.TrackingMode))
                    return SyncMutationResult.Rejected();
                workout.AddExercise(
                    exercise.WorkoutExerciseId,
                    exercise.ExerciseDefinitionId,
                    authoritativeModes[exercise.ExerciseDefinitionId],
                    order);
            }

            store.AddWorkout(workout);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }

    private static bool HasExactContiguousOrders(
        IReadOnlyList<StartWorkoutExerciseSyncPayload> exercises)
    {
        var seen = new bool[exercises.Count];
        foreach (var exercise in exercises)
        {
            if (exercise.Order is not int order
                || order < 0
                || order >= exercises.Count
                || seen[order])
            {
                return false;
            }

            seen[order] = true;
        }

        return true;
    }
}

internal sealed class SaveSetSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<SaveSetSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        SaveSetSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.SetId == Guid.Empty
            || payload.CompletedAt == default
            || payload.Order is null or < 0)
        {
            return SyncMutationResult.Rejected();
        }

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion)
            return SyncMutationResult.Conflict(workout.Version);

        var exercise = workout.Exercises.SingleOrDefault(item => item.Id == payload.WorkoutExerciseId);
        if (exercise is null || exercise.Sets.Count != payload.Order.Value)
            return SyncMutationResult.Rejected();
        if (!await store.IsSetIdentifierAvailableAsync(payload.SetId, cancellationToken))
            return SyncMutationResult.Rejected();

        try
        {
            workout.CompleteSet(
                payload.WorkoutExerciseId,
                payload.SetId,
                new SetMeasurement(ParseDecimal(payload.WeightKg), ParseDecimal(payload.AssistedKg), payload.Reps),
                payload.CompletedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (value is null) return null;
        return decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : throw new FormatException("The decimal value is invalid.");
    }
}

internal sealed class CompleteWorkoutSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<CompleteWorkoutSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        CompleteWorkoutSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty || payload.CompletedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion)
            return SyncMutationResult.Conflict(workout.Version);

        try
        {
            workout.Complete(payload.CompletedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal sealed class EditSetSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<EditSetSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        EditSetSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.SetId == Guid.Empty
            || payload.UpdatedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion)
            return SyncMutationResult.Conflict(workout.Version);

        try
        {
            workout.EditSet(
                payload.WorkoutExerciseId,
                payload.SetId,
                new SetMeasurement(
                    ParseDecimal(payload.WeightKg),
                    ParseDecimal(payload.AssistedKg),
                    payload.Reps),
                payload.UpdatedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }

    private static decimal? ParseDecimal(string? value) => DecimalParser.Parse(value);
}

internal sealed class DeleteSetSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<DeleteSetSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        DeleteSetSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.SetId == Guid.Empty
            || payload.DeletedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion)
            return SyncMutationResult.Conflict(workout.Version);

        try
        {
            workout.DeleteSet(payload.WorkoutExerciseId, payload.SetId, payload.DeletedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal sealed class DeleteWorkoutSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<DeleteWorkoutSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        DeleteWorkoutSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty || payload.DeletedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion)
            return SyncMutationResult.Conflict(workout.Version);

        try
        {
            workout.Delete(payload.DeletedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal sealed class AddExerciseSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<AddExerciseSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        AddExerciseSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.ExerciseDefinitionId == Guid.Empty
            || payload.Order is null or < 0
            || payload.AddedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion) return SyncMutationResult.Conflict(workout.Version);
        if (payload.AddedAt < workout.StartedAt) return SyncMutationResult.Rejected();

        var modes = await store.GetAvailableExerciseTrackingModesAsync(
            currentUser.UserId, [payload.ExerciseDefinitionId], cancellationToken);
        if (!modes.TryGetValue(payload.ExerciseDefinitionId, out var mode)
            || (int)mode != payload.TrackingMode)
            return SyncMutationResult.Rejected();
        if (!await store.AreWorkoutExerciseIdentifiersAvailableAsync(
                [payload.WorkoutExerciseId], cancellationToken))
            return SyncMutationResult.Rejected();

        try
        {
            workout.AddExercise(
                payload.WorkoutExerciseId,
                payload.ExerciseDefinitionId,
                mode,
                payload.Order.Value);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal sealed class RemoveExerciseSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<RemoveExerciseSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        RemoveExerciseSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.DeletedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion) return SyncMutationResult.Conflict(workout.Version);
        if (workout.Status != WorkoutStatus.Active)
            return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutAlreadyCompleted);

        try
        {
            workout.DeleteExercise(payload.WorkoutExerciseId, payload.DeletedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal sealed class ReorderExercisesSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<ReorderExercisesSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        ReorderExercisesSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseIds is null
            || payload.WorkoutExerciseIds.Count == 0
            || payload.ReorderedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion) return SyncMutationResult.Conflict(workout.Version);
        if (payload.ReorderedAt < workout.StartedAt) return SyncMutationResult.Rejected();

        try
        {
            workout.ReorderExercises(payload.WorkoutExerciseIds);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal sealed class DeleteWorkoutExerciseSyncHandler(
    ISyncPushStore store,
    ICurrentUser currentUser) : IRequestHandler<DeleteWorkoutExerciseSyncCommand, SyncMutationResult>
{
    public async Task<SyncMutationResult> Handle(
        DeleteWorkoutExerciseSyncCommand request,
        CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload.WorkoutId == Guid.Empty
            || payload.WorkoutExerciseId == Guid.Empty
            || payload.DeletedAt == default)
            return SyncMutationResult.Rejected();

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var workout = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (workout is null) return SyncMutationResult.Rejected(BusinessErrorCode.WorkoutNotFound);
        if (workout.Version != request.BaseVersion) return SyncMutationResult.Conflict(workout.Version);
        if (workout.Status != WorkoutStatus.Completed) return SyncMutationResult.Rejected();

        try
        {
            workout.DeleteExercise(payload.WorkoutExerciseId, payload.DeletedAt);
            return SyncMutationResult.Applied(workout);
        }
        catch (WorkoutRuleException exception)
        {
            return SyncMutationFailures.From(exception);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return SyncMutationResult.Rejected();
        }
    }
}

internal static class SyncMutationFailures
{
    public static SyncMutationResult From(WorkoutRuleException exception) =>
        SyncMutationResult.Rejected(exception.Violation switch
        {
            WorkoutRuleViolation.WorkoutAlreadyCompleted => BusinessErrorCode.WorkoutAlreadyCompleted,
            WorkoutRuleViolation.InvalidSetValue => BusinessErrorCode.InvalidSetValue,
            _ => BusinessErrorCode.InvalidRequest
        });
}

internal static class DecimalParser
{
    public static decimal? Parse(string? value)
    {
        if (value is null) return null;
        return decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : throw new FormatException("The decimal value is invalid.");
    }
}
