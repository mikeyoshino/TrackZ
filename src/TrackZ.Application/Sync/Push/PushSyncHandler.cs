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
            await transaction.CommitAsync(cancellationToken);
            return publicResult;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException && store.IsTransient(exception))
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
            if (operation.Action == "StartWorkout" && !HasStartWorkoutContract(operation.Payload)
                || operation.Action == "SaveSet" && !HasSaveSetContract(operation.Payload))
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
        if (request.BaseVersion != 0
            || payload.WorkoutId == Guid.Empty
            || payload.StartedAt == default
            || payload.Exercises is null
            || payload.Exercises.Count == 0
            || !HasExactContiguousOrders(payload.Exercises))
        {
            return SyncMutationResult.Rejected();
        }

        await store.AcquireWorkoutLockAsync(payload.WorkoutId, cancellationToken);
        var existing = await store.FindOwnedWorkoutAsync(
            currentUser.UserId, payload.WorkoutId, cancellationToken);
        if (existing is not null) return SyncMutationResult.Conflict(existing.Version);
        if (await store.FindWorkoutOwnerAsync(payload.WorkoutId, cancellationToken) is not null)
            return SyncMutationResult.Rejected();

        var exerciseIds = payload.Exercises.Select(exercise => exercise.ExerciseDefinitionId).ToArray();
        if (!await store.AreExerciseDefinitionsAvailableAsync(
                currentUser.UserId, exerciseIds, cancellationToken))
        {
            return SyncMutationResult.Rejected();
        }
        var workoutExerciseIds = payload.Exercises.Select(exercise => exercise.WorkoutExerciseId).ToArray();
        if (!await store.AreWorkoutExerciseIdentifiersAvailableAsync(
                workoutExerciseIds, cancellationToken))
        {
            return SyncMutationResult.Rejected();
        }

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
                    (TrackingMode)exercise.TrackingMode,
                    order);
            }

            store.AddWorkout(workout);
            return SyncMutationResult.Applied(workout);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or WorkoutRuleException)
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
        if (workout is null) return SyncMutationResult.Rejected();
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
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or WorkoutRuleException)
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
