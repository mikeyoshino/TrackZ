using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Sync;

public interface ISyncApi
{
    Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken cancellationToken = default);
    Task<SyncPullResponse> PullAsync(string? cursor, CancellationToken cancellationToken = default);
}

public enum SyncApiFailureKind
{
    Authentication = 1,
    Retryable = 2,
    Permanent = 3,
    InvalidCursor = 4
}

public sealed class SyncApiException(
    SyncApiFailureKind kind,
    HttpStatusCode statusCode,
    BusinessErrorCode? errorCode,
    string? traceId,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public SyncApiFailureKind Kind { get; } = kind;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public BusinessErrorCode? ErrorCode { get; } = errorCode;
    public string? TraceId { get; } = traceId;
}

public sealed class TrackZSyncApiClient(HttpClient httpClient) : ISyncApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SyncPushResponse> PushAsync(
        SyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/sync/push", request, JsonOptions, cancellationToken);
        return await ReadAsync<SyncPushResponse>(response, false, cancellationToken);
    }

    public async Task<SyncPullResponse> PullAsync(
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/sync/pull?pageSize=100" +
            (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        using var response = await httpClient.GetAsync(path, cancellationToken);
        return await ReadAsync<SyncPullResponse>(response, cursor is not null, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response,
        bool isPersistedCursorPull,
        CancellationToken cancellationToken) where T : class
    {
        if (!response.IsSuccessStatusCode)
        {
            ApiProblemDetails? problem = null;
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(
                    JsonOptions, cancellationToken);
            }
            catch (Exception exception) when (
                exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                // Status remains authoritative even when an intermediary replaced the problem body.
            }

            var kind = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    SyncApiFailureKind.Authentication,
                HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests =>
                    SyncApiFailureKind.Retryable,
                >= HttpStatusCode.InternalServerError => SyncApiFailureKind.Retryable,
                HttpStatusCode.BadRequest when isPersistedCursorPull
                    && problem?.ErrorCode == BusinessErrorCode.InvalidRequest =>
                    SyncApiFailureKind.InvalidCursor,
                _ => SyncApiFailureKind.Permanent
            };
            throw new SyncApiException(
                kind,
                response.StatusCode,
                problem?.ErrorCode,
                problem?.TraceId,
                problem?.Message ?? "The sync endpoint rejected the request.");
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The sync response was empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("The sync response was malformed.", exception);
        }
    }
}

public enum SyncRunStatus
{
    Completed = 1,
    Offline = 2,
    AuthenticationRequired = 3,
    RetryScheduled = 4,
    PermanentFailure = 5
}

public sealed class SyncCoordinator(
    TrackZLocalDatabase database,
    ISyncApi api,
    IAccountSessionBoundary sessionBoundary,
    IClock clock)
{
    private const string CursorScope = "workouts";
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public async Task<SyncRunStatus> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var generation = sessionBoundary.Capture();
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            using var lease = sessionBoundary.CreateCancellationLease(generation, cancellationToken);
            try
            {
                var pushWasRejected = false;
                while (await ReadNextPendingAsync(
                           generation, clock.UtcNow, cancellationToken) is { } operation)
                {
                    await MarkSendingAsync(generation, operation.OperationId, cancellationToken);
                    var request = new SyncPushRequest([ToDto(operation)]);
                    SyncPushResponse response;
                    try
                    {
                        response = await api.PushAsync(request, lease.Token);
                    }
                    catch (SyncApiException exception) when (
                        exception.Kind == SyncApiFailureKind.Retryable)
                    {
                        await CommitPushResultsAsync(generation, [operation], [new SyncOperationResultDto(
                            operation.OperationId,
                            SyncOperationStatus.Retryable,
                            null,
                            BusinessErrorCode.InternalServerError)], cancellationToken);
                        return SyncRunStatus.RetryScheduled;
                    }
                    catch (SyncApiException exception) when (
                        exception.Kind == SyncApiFailureKind.Authentication)
                    {
                        return SyncRunStatus.AuthenticationRequired;
                    }
                    catch (SyncApiException exception)
                    {
                        var errorCode = exception.ErrorCode is { } candidate
                            && Enum.IsDefined(candidate)
                            && candidate is not (
                                BusinessErrorCode.VersionConflict
                                or BusinessErrorCode.InternalServerError)
                                ? candidate
                                : BusinessErrorCode.InvalidRequest;
                        await CommitPushResultsAsync(generation, [operation], [
                            new SyncOperationResultDto(
                                operation.OperationId,
                                SyncOperationStatus.Rejected,
                                null,
                                errorCode)
                        ], cancellationToken);
                        return SyncRunStatus.PermanentFailure;
                    }
                    ValidateResults([operation], response);
                    await CommitPushResultsAsync(
                        generation, [operation], response.Results, cancellationToken);
                    if (response.Results[0].Status == SyncOperationStatus.Rejected)
                    {
                        pushWasRejected = true;
                        break;
                    }
                    if (response.Results[0].Status != SyncOperationStatus.Applied) break;
                }

                var cursor = await ReadCursorAsync(generation, cancellationToken);
                do
                {
                    SyncPullResponse response;
                    var requestedCursor = cursor;
                    try
                    {
                        response = await api.PullAsync(requestedCursor, lease.Token);
                    }
                    catch (SyncApiException exception) when (
                        exception.Kind == SyncApiFailureKind.InvalidCursor
                        && requestedCursor is not null)
                    {
                        try
                        {
                            response = await api.PullAsync(null, lease.Token);
                            requestedCursor = null;
                        }
                        catch (SyncApiException retryException) when (
                            retryException.Kind == SyncApiFailureKind.Authentication)
                        {
                            return SyncRunStatus.AuthenticationRequired;
                        }
                        catch (SyncApiException retryException) when (
                            retryException.Kind == SyncApiFailureKind.Retryable)
                        {
                            return SyncRunStatus.RetryScheduled;
                        }
                        catch (SyncApiException)
                        {
                            return SyncRunStatus.PermanentFailure;
                        }
                    }
                    catch (SyncApiException exception) when (
                        exception.Kind == SyncApiFailureKind.Authentication)
                    {
                        return SyncRunStatus.AuthenticationRequired;
                    }
                    catch (SyncApiException exception) when (
                        exception.Kind == SyncApiFailureKind.Retryable)
                    {
                        return SyncRunStatus.RetryScheduled;
                    }
                    catch (SyncApiException)
                    {
                        return SyncRunStatus.PermanentFailure;
                    }
                    ValidatePull(response, requestedCursor);
                    await CommitPullAsync(generation, response, cancellationToken);
                    cursor = response.NextCursor;
                    if (!response.HasMore) break;
                } while (true);
                return pushWasRejected
                    ? SyncRunStatus.PermanentFailure
                    : SyncRunStatus.Completed;
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                || exception is OperationCanceledException
                    && !cancellationToken.IsCancellationRequested
                    && !sessionBoundary.IsCancellationRequested(generation))
            {
                return SyncRunStatus.Offline;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    internal async Task<OutboxOperation> RebaseAsync(
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An operation is required.", nameof(operationId));
        if (serverVersion < 0) throw new ArgumentOutOfRangeException(nameof(serverVersion));
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionBoundary.Capture();
            OutboxOperation? replacement = null;
            return await CompleteAsync();

            async Task<OutboxOperation> CompleteAsync()
            {
                var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
                {
                    replacement = await database.WriteAsync(async (connection, transaction, innerToken) =>
                    {
                        var original = await ReadOperationAsync(connection, transaction, operationId, innerToken);
                        if (original.State != OutboxOperationState.Conflicted)
                            throw new InvalidOperationException("Only a conflicted operation can be rebased.");
                        var latest = await ReadLatestCreatedAtAsync(connection, transaction, innerToken);
                        var now = clock.UtcNow.ToUniversalTime();
                        var createdAt = now > latest ? now : latest.AddTicks(1);
                        if (original.ServerVersion != serverVersion
                            || original.ServerPayload is null)
                            throw new InvalidOperationException(
                                "This conflict cannot be rebased against the selected server version.");
                        if (await HasLiveReplacementAsync(
                                connection, transaction, original.OperationId, innerToken))
                            throw new InvalidOperationException("The conflict already has a live replacement.");
                        if (await HasUnrelatedLivePendingSuccessorAsync(
                                connection, transaction, original,
                                new HashSet<Guid> { original.OperationId }, innerToken))
                            throw new InvalidOperationException(
                                "Resolve later sync intent before rebasing this conflict.");
                        var server = JsonSerializer.Deserialize<SyncWorkoutDto>(
                            original.ServerPayload, JsonOptions)
                            ?? throw new InvalidDataException("The stored server authority is malformed.");
                        ValidateGraph(server);
                        var serverMutationAt = LastServerMutationAt(server);
                        if (createdAt <= serverMutationAt) createdAt = serverMutationAt.AddTicks(1);
                        var rebasedPayload = RebasePayload(original, server, createdAt);
                        var next = original with
                        {
                            OperationId = Guid.NewGuid(),
                            Payload = JsonSerializer.Serialize(rebasedPayload, JsonOptions),
                            BaseVersion = serverVersion,
                            CreatedAt = createdAt,
                            State = OutboxOperationState.Pending,
                            DeletedAt = null,
                            Version = 1,
                            ServerVersion = null,
                            RetryCount = 0,
                            NextAttemptAt = null,
                            ServerPayload = null,
                            ReplacesOperationId = original.OperationId,
                            SendStartedAt = null,
                            NeutralizedAt = null
                        };
                        await InsertOperationAsync(connection, transaction, next, innerToken);
                        await ExecuteAsync(connection, transaction, """
                            UPDATE HistoryUndo
                            SET OperationId = $replacementId
                            WHERE OperationId = $originalId;
                            """, innerToken,
                            ("$replacementId", Id(next.OperationId)),
                            ("$originalId", Id(original.OperationId)));
                        return next;
                    }, token);
                }, cancellationToken);
                EnsureCurrent(committed, generation, cancellationToken);
                return replacement!;
            }
        }
        finally
        {
            _runGate.Release();
        }
    }

    private static object RebasePayload(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt) => original.Type switch
    {
        OutboxOperationType.StartWorkout => RebaseStartWorkout(original, server),
        OutboxOperationType.SaveSet => RebaseSaveSet(original, server),
        OutboxOperationType.CompleteWorkout => RebaseComplete(original, server, mutationAt),
        OutboxOperationType.EditSet => RebaseEditSet(original, server, mutationAt),
        OutboxOperationType.DeleteSet => RebaseDeleteSet(original, server, mutationAt),
        OutboxOperationType.DeleteWorkout => RebaseDeleteWorkout(original, server, mutationAt),
        OutboxOperationType.AddExercise => RebaseAddExercise(original, server, mutationAt),
        OutboxOperationType.RemoveExercise => RebaseRemoveExercise(original, server, mutationAt),
        OutboxOperationType.ReorderExercises => RebaseReorderExercises(original, server, mutationAt),
        OutboxOperationType.DeleteWorkoutExercise =>
            RebaseDeleteWorkoutExercise(original, server, mutationAt),
        _ => throw new InvalidOperationException("This operation cannot be rebased safely.")
    };

    private static StartWorkoutOutboxPayload RebaseStartWorkout(
        OutboxOperation original,
        SyncWorkoutDto server)
    {
        var payload = original.DeserializePayload<StartWorkoutOutboxPayload>();
        if (payload.WorkoutId != server.Id
            || server.Status != 2
            || server.DeletedAt is not null)
            throw new InvalidOperationException(
                "The server workout can no longer accept the local start selection.");
        foreach (var requested in payload.Exercises)
        {
            var existingIdentity = server.Exercises.SingleOrDefault(item =>
                item.Id == requested.WorkoutExerciseId);
            if (existingIdentity is not null
                && (existingIdentity.DeletedAt is not null
                    || existingIdentity.ExerciseDefinitionId != requested.ExerciseDefinitionId
                    || existingIdentity.TrackingMode != requested.TrackingMode))
                throw new InvalidOperationException(
                    "A local workout exercise identity conflicts with server authority.");
            if (existingIdentity is null
                && server.Exercises.Any(item => item.DeletedAt is null
                    && item.ExerciseDefinitionId == requested.ExerciseDefinitionId))
                throw new InvalidOperationException(
                    "The exercise already exists on the server under a different identity.");
        }
        return payload with { StartedAt = server.StartedAt };
    }

    private static AddExerciseOutboxPayload RebaseAddExercise(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<AddExerciseOutboxPayload>();
        if (server.Status != 2 || server.DeletedAt is not null
            || server.Exercises.Any(item => item.Id == payload.WorkoutExerciseId)
            || server.Exercises.Any(item => item.DeletedAt is null
                && item.ExerciseDefinitionId == payload.ExerciseDefinitionId))
            throw new InvalidOperationException("The exercise can no longer be added to the server workout.");
        return payload with
        {
            Order = Math.Min(payload.Order, server.Exercises.Count(item => item.DeletedAt is null)),
            AddedAt = mutationAt
        };
    }

    private static RemoveExerciseOutboxPayload RebaseRemoveExercise(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<RemoveExerciseOutboxPayload>();
        if (server.Status != 2 || server.DeletedAt is not null)
            throw new InvalidOperationException("The server workout is no longer active.");
        _ = RequiredActiveExercise(server, payload.WorkoutExerciseId);
        return payload with { DeletedAt = mutationAt };
    }

    private static ReorderExercisesOutboxPayload RebaseReorderExercises(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<ReorderExercisesOutboxPayload>();
        if (server.Status != 2 || server.DeletedAt is not null)
            throw new InvalidOperationException("The server workout is no longer active.");
        var activeIds = server.Exercises.Where(item => item.DeletedAt is null)
            .OrderBy(item => item.Order).Select(item => item.Id).ToArray();
        var activeSet = activeIds.ToHashSet();
        var desired = payload.WorkoutExerciseIds.Where(activeSet.Contains).ToList();
        desired.AddRange(activeIds.Where(id => !desired.Contains(id)));
        if (desired.Count != activeIds.Length)
            throw new InvalidOperationException("The exercise order cannot be reconciled.");
        return payload with { WorkoutExerciseIds = desired, ReorderedAt = mutationAt };
    }

    private static DeleteWorkoutExerciseOutboxPayload RebaseDeleteWorkoutExercise(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<DeleteWorkoutExerciseOutboxPayload>();
        if (server.Status != 3 || server.DeletedAt is not null)
            throw new InvalidOperationException("The server workout is not editable history.");
        _ = RequiredActiveExercise(server, payload.WorkoutExerciseId);
        return payload with { DeletedAt = mutationAt };
    }

    private static SaveSetOutboxPayload RebaseSaveSet(
        OutboxOperation original,
        SyncWorkoutDto server)
    {
        var payload = original.DeserializePayload<SaveSetOutboxPayload>();
        var exercise = RequiredActiveExercise(server, payload.WorkoutExerciseId);
        if (exercise.Sets.Any(set => set.Id == payload.SetId && set.DeletedAt is null))
            throw new InvalidOperationException("The local set identifier already exists on the server.");
        return payload with { Order = exercise.Sets.Count(set => set.DeletedAt is null) };
    }

    private static CompleteWorkoutOutboxPayload RebaseComplete(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<CompleteWorkoutOutboxPayload>();
        if (payload.WorkoutId != server.Id || server.Status != 2 || server.DeletedAt is not null)
            throw new InvalidOperationException("The server workout can no longer be completed.");
        return payload with { CompletedAt = mutationAt };
    }

    private static EditSetOutboxPayload RebaseEditSet(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<EditSetOutboxPayload>();
        var exercise = RequiredActiveExercise(server, payload.WorkoutExerciseId);
        _ = exercise.Sets.SingleOrDefault(set =>
                set.Id == payload.SetId && set.DeletedAt is null)
            ?? throw new InvalidOperationException("The server set can no longer be edited.");
        return payload with { UpdatedAt = mutationAt };
    }

    private static DeleteSetOutboxPayload RebaseDeleteSet(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<DeleteSetOutboxPayload>();
        var exercise = RequiredActiveExercise(server, payload.WorkoutExerciseId);
        _ = exercise.Sets.SingleOrDefault(set =>
                set.Id == payload.SetId && set.DeletedAt is null)
            ?? throw new InvalidOperationException("The server set can no longer be deleted.");
        return payload with { DeletedAt = mutationAt };
    }

    private static DeleteWorkoutOutboxPayload RebaseDeleteWorkout(
        OutboxOperation original,
        SyncWorkoutDto server,
        DateTimeOffset mutationAt)
    {
        var payload = original.DeserializePayload<DeleteWorkoutOutboxPayload>();
        if (payload.WorkoutId != server.Id || server.DeletedAt is not null)
            throw new InvalidOperationException("The server workout is already deleted.");
        return payload with { DeletedAt = mutationAt };
    }

    private static SyncWorkoutExerciseDto RequiredActiveExercise(
        SyncWorkoutDto server,
        Guid workoutExerciseId) =>
        server.Exercises.SingleOrDefault(exercise =>
            exercise.Id == workoutExerciseId && exercise.DeletedAt is null)
        ?? throw new InvalidOperationException("The server exercise no longer accepts this change.");

    private static DateTimeOffset LastServerMutationAt(SyncWorkoutDto workout)
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

    internal async Task KeepServerAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An operation is required.", nameof(operationId));
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var generation = sessionBoundary.Capture();
            var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
            {
                await database.WriteAsync(async (connection, transaction, innerToken) =>
                {
                    var operation = await ReadOperationAsync(connection, transaction, operationId, innerToken);
                    if (operation.State != OutboxOperationState.Conflicted)
                        throw new InvalidOperationException("The conflicted operation has no server authority to keep.");
                    var authority = operation;
                    var leaf = operation;
                    var chain = new HashSet<Guid> { operation.OperationId };
                    var chainOperations = new Dictionary<Guid, OutboxOperation>
                    {
                        [operation.OperationId] = operation
                    };
                    while (await ReadLiveReplacementAsync(
                               connection, transaction, leaf.OperationId, innerToken) is { } replacement)
                    {
                        if (!chain.Add(replacement.OperationId))
                            throw new InvalidDataException("The conflict replacement chain is cyclic.");
                        chainOperations.Add(replacement.OperationId, replacement);
                        leaf = replacement;
                        if (replacement.ServerPayload is not null) authority = replacement;
                    }
                    var ancestorId = leaf.ReplacesOperationId;
                    var visitedAncestors = new HashSet<Guid>();
                    while (ancestorId is { } id)
                    {
                        if (!visitedAncestors.Add(id))
                            throw new InvalidDataException("The conflict replacement chain is cyclic.");
                        chain.Add(id);
                        var ancestor = await ReadOperationAsync(
                            connection, transaction, id, innerToken);
                        chainOperations.TryAdd(id, ancestor);
                        ancestorId = ancestor.ReplacesOperationId;
                    }
                    if (chainOperations.Values.Any(candidate =>
                            candidate.State == OutboxOperationState.Pending
                            && candidate.SendStartedAt is not null))
                        throw new InvalidOperationException(
                            "A replacement operation has an ambiguous server outcome and must be reconciled first.");
                    if (await HasUnrelatedLivePendingSuccessorAsync(
                            connection, transaction, operation, chain, innerToken))
                        throw new InvalidOperationException(
                            "Resolve later sync intent before keeping server authority.");
                    if (authority.ServerPayload is null)
                        throw new InvalidOperationException("The conflicted operation has no server authority to keep.");
                    var graph = JsonSerializer.Deserialize<SyncWorkoutDto>(authority.ServerPayload, JsonOptions)
                        ?? throw new InvalidDataException("The stored server authority is malformed.");
                    var resolvedAt = clock.UtcNow;
                    await ArchiveAsync(
                        connection, transaction, leaf.OperationId,
                        OutboxOperationState.Rejected, resolvedAt, innerToken);
                    await ArchiveReplacementAncestorsAsync(
                        connection, transaction, leaf, resolvedAt, innerToken);
                    await ApplyGraphAsync(connection, transaction, graph, innerToken);
                    await NeutralizeResolvedChainAsync(
                        connection, transaction, chain, resolvedAt, innerToken);
                    await DeleteHistoryUndoAsync(
                        connection, transaction, chain, innerToken);
                    return true;
                }, token);
            }, cancellationToken);
            EnsureCurrent(committed, generation, cancellationToken);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<OutboxOperation?> ReadNextPendingAsync(
        AccountSessionGeneration generation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        OutboxOperation? result = null;
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            result = await database.ReadAsync(async (connection, innerToken) =>
            {
                var rows = new List<OutboxOperation>();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                           CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                           NextAttemptAt, ServerPayload, ReplacesOperationId
                           , SendStartedAt, NeutralizedAt, FailureCode
                    FROM OutboxOperation
                    WHERE State IN (1, 4) AND DeletedAt IS NULL
                    ORDER BY CreatedAt, OperationId;
                    """;
                await using var reader = await command.ExecuteReaderAsync(innerToken);
                while (await reader.ReadAsync(innerToken)) rows.Add(ReadOperation(reader));
                if (rows.Count == 0) return null;

                var runnable = new List<OutboxOperation>();
                foreach (var aggregateRows in rows
                             .GroupBy(item => item.EntityId)
                             .Select(group => group.OrderBy(item => item.CreatedAt)
                                 .ThenBy(item => item.OperationId).ToList()))
                {
                    var candidate = aggregateRows[0];
                    var visited = new HashSet<Guid>();
                    while (candidate.State == OutboxOperationState.Conflicted)
                    {
                        if (!visited.Add(candidate.OperationId))
                            throw new InvalidDataException("The conflict replacement chain is cyclic.");
                        var replacements = aggregateRows.Where(item =>
                            item.ReplacesOperationId == candidate.OperationId).ToArray();
                        if (replacements.Length > 1)
                            throw new InvalidDataException(
                                "A conflict has multiple live replacements.");
                        var replacement = replacements.SingleOrDefault();
                        if (replacement is null)
                        {
                            candidate = null!;
                            break;
                        }
                        candidate = replacement;
                    }

                    if (candidate is not null
                        && (candidate.NextAttemptAt is null || candidate.NextAttemptAt <= now))
                        runnable.Add(candidate);
                }
                return runnable.OrderBy(item => item.CreatedAt).ThenBy(item => item.OperationId)
                    .FirstOrDefault();
            }, token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
        return result;
    }

    private async Task CommitPushResultsAsync(
        AccountSessionGeneration generation,
        IReadOnlyList<OutboxOperation> operations,
        IReadOnlyList<SyncOperationResultDto> results,
        CancellationToken cancellationToken)
    {
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            await database.WriteAsync(async (connection, transaction, innerToken) =>
            {
                for (var index = 0; index < operations.Count; index++)
                {
                    var operation = operations[index];
                    var result = results[index];
                    switch (result.Status)
                    {
                        case SyncOperationStatus.Applied:
                        case SyncOperationStatus.Rejected:
                            await ArchiveAsync(
                                connection, transaction, operation.OperationId,
                                result.Status == SyncOperationStatus.Applied
                                    ? OutboxOperationState.Applied
                                    : OutboxOperationState.Rejected,
                                clock.UtcNow, innerToken, result.ServerVersion,
                                result.Status == SyncOperationStatus.Rejected
                                    ? result.ErrorCode
                                    : null);
                            if (result.Status == SyncOperationStatus.Applied)
                                await ArchiveReplacementAncestorsAsync(
                                    connection, transaction, operation,
                                    clock.UtcNow, innerToken);
                            break;
                        case SyncOperationStatus.Conflict:
                            await UpdateConflictAsync(
                                connection, transaction, operation.OperationId,
                                result.ServerVersion!.Value, innerToken);
                            break;
                        case SyncOperationStatus.Retryable:
                            await UpdateRetryAsync(connection, transaction, operation, innerToken);
                            break;
                        default:
                            throw new InvalidDataException("The sync status is invalid.");
                    }
                }
                return true;
            }, token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
    }

    private async Task<string?> ReadCursorAsync(
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            cursor = await database.ReadAsync(async (connection, innerToken) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT Cursor FROM SyncCursor WHERE Scope = $scope;";
                command.Parameters.AddWithValue("$scope", CursorScope);
                return (string?)await command.ExecuteScalarAsync(innerToken);
            }, token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
        return cursor;
    }

    private async Task CommitPullAsync(
        AccountSessionGeneration generation,
        SyncPullResponse response,
        CancellationToken cancellationToken)
    {
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            await database.WriteAsync(async (connection, transaction, innerToken) =>
            {
                foreach (var change in response.Changes)
                {
                    ValidateGraph(change.Workout);
                    await ValidateGraphIdentityAsync(
                        connection, transaction, change.Workout, innerToken);
                    var serverPayload = JsonSerializer.Serialize(change.Workout, JsonOptions);
                    var conflicts = await ReadActiveOperationsAsync(
                        connection, transaction, change.EntityId, innerToken);
                    if (conflicts.Count > 0)
                    {
                        foreach (var operation in conflicts.Where(operation =>
                                     change.ServerVersion > operation.BaseVersion))
                            await StoreServerConflictAsync(
                                connection, transaction, operation.OperationId,
                                change.ServerVersion, serverPayload, innerToken);
                    }
                    else
                    {
                        await ApplyGraphAsync(connection, transaction, change.Workout, innerToken);
                        await PurgeAcknowledgedHistoryUndoAsync(
                            connection, transaction, change.EntityId,
                            change.ServerVersion, innerToken);
                    }
                }
                if (response.NextCursor is not null)
                    await WriteCursorAsync(
                        connection, transaction, response.NextCursor,
                        clock.UtcNow, innerToken);
                else
                    await ExecuteAsync(connection, transaction, """
                        DELETE FROM SyncCursor WHERE Scope = $scope;
                        """, innerToken, ("$scope", CursorScope));
                return true;
            }, token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
    }

    private static SyncOperationDto ToDto(OutboxOperation operation)
    {
        using var document = JsonDocument.Parse(operation.Payload);
        return new SyncOperationDto(
            operation.OperationId,
            "Workout",
            operation.Type.ToString(),
            document.RootElement.Clone(),
            operation.BaseVersion);
    }

    private static void ValidateResults(
        IReadOnlyList<OutboxOperation> operations,
        SyncPushResponse response)
    {
        if (response.Results is null || response.Results.Count != operations.Count)
            throw new InvalidDataException("The push result count is invalid.");
        var seen = new HashSet<Guid>();
        for (var index = 0; index < operations.Count; index++)
        {
            var result = response.Results[index];
            if (result is null
                || result.OperationId != operations[index].OperationId
                || !seen.Add(result.OperationId)
                || !Enum.IsDefined(result.Status)
                || !HasValidResultContract(result))
                throw new InvalidDataException("The push result mapping is invalid.");
        }

        static bool HasValidResultContract(SyncOperationResultDto result) => result.Status switch
        {
            SyncOperationStatus.Applied =>
                result.ServerVersion is > 0 && result.ErrorCode is null,
            SyncOperationStatus.Rejected =>
                result.ServerVersion is null
                && result.ErrorCode is { } rejectedCode
                && Enum.IsDefined(rejectedCode)
                && rejectedCode is not (
                    BusinessErrorCode.VersionConflict or BusinessErrorCode.InternalServerError),
            SyncOperationStatus.Conflict =>
                result.ServerVersion is > 0
                && result.ErrorCode == BusinessErrorCode.VersionConflict,
            SyncOperationStatus.Retryable =>
                result.ServerVersion is null
                && result.ErrorCode == BusinessErrorCode.InternalServerError,
            _ => false
        };
    }

    private static void ValidatePull(SyncPullResponse response, string? priorCursor)
    {
        if (response.Changes is null
            || response.HasMore && response.Changes.Count == 0
            || response.Changes.Count > 0 && string.IsNullOrWhiteSpace(response.NextCursor)
            || response.Changes.Count > 0 && response.NextCursor == priorCursor
            || response.Changes.Count == 0 && response.NextCursor != priorCursor)
            throw new InvalidDataException("The pull page is malformed.");
        long priorSequence = 0;
        foreach (var change in response.Changes)
        {
            if (change is null
                || change.Sequence <= priorSequence
                || change.EntityType != "Workout"
                || change.EntityId == Guid.Empty
                || change.Workout is null
                || change.Workout.Id != change.EntityId
                || change.Workout.Version != change.ServerVersion
                || change.IsDeleted != (change.Workout.DeletedAt is not null))
                throw new InvalidDataException("The pull change is malformed.");
            priorSequence = change.Sequence;
        }
    }

    private void EnsureCurrent(
        bool committed,
        AccountSessionGeneration generation,
        CancellationToken cancellationToken)
    {
        if (committed && !sessionBoundary.IsCancellationRequested(generation)) return;
        throw new OperationCanceledException("The account session changed.", cancellationToken);
    }

    private async Task UpdateRetryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation operation,
        CancellationToken cancellationToken)
    {
        var retryCount = checked(operation.RetryCount + 1);
        var delaySeconds = Math.Min(3600, 1L << Math.Min(12, retryCount - 1));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE OutboxOperation
            SET RetryCount = $retryCount, NextAttemptAt = $next, SendStartedAt = NULL,
                Version = Version + 1
            WHERE OperationId = $id AND State = 1 AND DeletedAt IS NULL;
            """;
        Add(command, "$retryCount", retryCount);
        Add(command, "$next", Timestamp(clock.UtcNow.AddSeconds(delaySeconds)));
        Add(command, "$id", Id(operation.OperationId));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("The retryable operation changed concurrently.");
    }

    private async Task MarkSendingAsync(
        AccountSessionGeneration generation,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var committed = await sessionBoundary.TryCommitAsync(generation, async token =>
        {
            await database.WriteAsync(async (connection, transaction, innerToken) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE OutboxOperation
                    SET SendStartedAt = $startedAt, Version = Version + 1
                    WHERE OperationId = $id AND State = 1 AND DeletedAt IS NULL
                      AND NeutralizedAt IS NULL
                      AND EXISTS (
                          SELECT 1 FROM HistoryUndo
                          WHERE HistoryUndo.OperationId = OutboxOperation.OperationId
                      );
                    """;
                Add(command, "$startedAt", Timestamp(clock.UtcNow));
                Add(command, "$id", Id(operationId));
                _ = await command.ExecuteNonQueryAsync(innerToken);
                return true;
            }, token);
        }, cancellationToken);
        EnsureCurrent(committed, generation, cancellationToken);
    }

    private static async Task PurgeAcknowledgedHistoryUndoAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid entityId,
        long serverVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM HistoryUndo
            WHERE OperationId IN (
                SELECT OperationId
                FROM OutboxOperation
                WHERE EntityId = $entityId AND State = 2
                  AND NeutralizedAt IS NULL
                  AND ServerVersion IS NOT NULL AND ServerVersion <= $serverVersion
            );
            """;
        Add(command, "$entityId", Id(entityId));
        Add(command, "$serverVersion", serverVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteHistoryUndoAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<Guid> operationIds,
        CancellationToken cancellationToken)
    {
        foreach (var operationId in operationIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM HistoryUndo WHERE OperationId = $id;";
            Add(command, "$id", Id(operationId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task NeutralizeResolvedChainAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<Guid> operationIds,
        DateTimeOffset neutralizedAt,
        CancellationToken cancellationToken)
    {
        foreach (var operationId in operationIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE OutboxOperation
                SET NeutralizedAt = $neutralizedAt, SendStartedAt = NULL,
                    NextAttemptAt = NULL, Version = Version + 1
                WHERE OperationId = $id AND NeutralizedAt IS NULL;
                """;
            Add(command, "$neutralizedAt", Timestamp(neutralizedAt));
            Add(command, "$id", Id(operationId));
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidDataException("The resolved operation changed concurrently.");
        }
    }

    private static async Task UpdateConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        long serverVersion,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE OutboxOperation
            SET State = 4, ServerVersion = $serverVersion,
                NextAttemptAt = NULL, Version = Version + 1
            WHERE OperationId = $id AND State = 1 AND DeletedAt IS NULL;
            """;
        Add(command, "$serverVersion", serverVersion);
        Add(command, "$id", Id(operationId));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("The conflicted operation changed concurrently.");
    }

    private static async Task ArchiveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        OutboxOperationState state,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        long? serverVersion = null,
        BusinessErrorCode? failureCode = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE OutboxOperation
            SET State = $state, DeletedAt = $deletedAt, ServerVersion = COALESCE($serverVersion, ServerVersion),
                NextAttemptAt = NULL, SendStartedAt = NULL,
                FailureCode = $failureCode, Version = Version + 1
            WHERE OperationId = $id AND DeletedAt IS NULL;
            """;
        Add(command, "$state", (int)state);
        Add(command, "$deletedAt", Timestamp(now));
        Add(command, "$serverVersion", serverVersion);
        Add(command, "$failureCode", failureCode is null ? null : (int)failureCode.Value);
        Add(command, "$id", Id(operationId));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidDataException("The terminal operation changed concurrently.");
    }

    private static async Task ArchiveReplacementAncestorsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation operation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var ancestorId = operation.ReplacesOperationId;
        var visited = new HashSet<Guid>();
        while (ancestorId is { } id)
        {
            if (!visited.Add(id))
                throw new InvalidDataException("The conflict replacement chain is cyclic.");
            var ancestor = await ReadOperationAsync(connection, transaction, id, cancellationToken);
            if (ancestor.DeletedAt is null)
                await ArchiveAsync(
                    connection, transaction, id,
                    OutboxOperationState.Applied, now, cancellationToken);
            ancestorId = ancestor.ReplacesOperationId;
        }
    }

    private static async Task<bool> HasLiveReplacementAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM OutboxOperation
                WHERE ReplacesOperationId = $id AND DeletedAt IS NULL
                  AND State IN (1, 4)
            );
            """;
        Add(command, "$id", Id(operationId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<OutboxOperation?> ReadLiveReplacementAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                   CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                   NextAttemptAt, ServerPayload, ReplacesOperationId
                   , SendStartedAt, NeutralizedAt, FailureCode
            FROM OutboxOperation
            WHERE ReplacesOperationId = $id AND DeletedAt IS NULL AND State IN (1, 4);
            """;
        Add(command, "$id", Id(operationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var replacement = ReadOperation(reader);
        if (await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("A conflict has multiple live replacements.");
        return replacement;
    }

    private static async Task StoreServerConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        long serverVersion,
        string serverPayload,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE OutboxOperation
            SET State = 4, ServerVersion = $version, ServerPayload = $payload,
                NextAttemptAt = NULL, Version = Version + 1
            WHERE OperationId = $id AND DeletedAt IS NULL AND State IN (1, 4)
              AND (ServerVersion IS NULL OR $version >= ServerVersion);
            """;
        Add(command, "$version", serverVersion);
        Add(command, "$payload", serverPayload);
        Add(command, "$id", Id(operationId));
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<OutboxOperation>> ReadActiveOperationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var operations = new List<OutboxOperation>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                   CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                   NextAttemptAt, ServerPayload, ReplacesOperationId
                   , SendStartedAt, NeutralizedAt, FailureCode
            FROM OutboxOperation
            WHERE EntityId = $entityId AND DeletedAt IS NULL AND State IN (1, 4)
            ORDER BY CreatedAt, OperationId;
            """;
        Add(command, "$entityId", Id(entityId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) operations.Add(ReadOperation(reader));
        return operations;
    }

    private static async Task ApplyGraphAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncWorkoutDto graph,
        CancellationToken cancellationToken)
    {
        ValidateGraph(graph);
        await ValidateGraphIdentityAsync(connection, transaction, graph, cancellationToken);
        await DeleteAbsentChildrenAsync(connection, transaction, graph, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE LocalWorkoutExercise SET SortOrder = SortOrder + 1000000000
            WHERE WorkoutId = $workoutId AND DeletedAt IS NULL;
            UPDATE LocalSet SET SortOrder = SortOrder + 1000000000
            WHERE DeletedAt IS NULL AND WorkoutExerciseId IN
                (SELECT Id FROM LocalWorkoutExercise WHERE WorkoutId = $workoutId);
            """, cancellationToken, ("$workoutId", Id(graph.Id)));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO LocalWorkout
                (Id, Status, StartedAt, CompletedAt, DeletedAt, Version, BaseVersion)
            VALUES ($id, $status, $startedAt, $completedAt, $deletedAt, $version, $version)
            ON CONFLICT(Id) DO UPDATE SET
                Status = excluded.Status, StartedAt = excluded.StartedAt,
                CompletedAt = excluded.CompletedAt, DeletedAt = excluded.DeletedAt,
                Version = excluded.Version, BaseVersion = excluded.BaseVersion;
            """, cancellationToken,
            ("$id", Id(graph.Id)), ("$status", graph.Status),
            ("$startedAt", Timestamp(graph.StartedAt)), ("$completedAt", Timestamp(graph.CompletedAt)),
            ("$deletedAt", Timestamp(graph.DeletedAt)), ("$version", graph.Version));
        foreach (var exercise in graph.Exercises)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO LocalWorkoutExercise
                    (Id, WorkoutId, ExerciseDefinitionId, TrackingMode, SortOrder,
                     DeletedAt, Version, BaseVersion)
                VALUES ($id, $workoutId, $definitionId, $mode, $order,
                        $deletedAt, $version, $version)
                ON CONFLICT(Id) DO UPDATE SET
                    TrackingMode = excluded.TrackingMode, SortOrder = excluded.SortOrder,
                    DeletedAt = excluded.DeletedAt, Version = excluded.Version,
                    BaseVersion = excluded.BaseVersion;
                """, cancellationToken,
                ("$id", Id(exercise.Id)), ("$workoutId", Id(graph.Id)),
                ("$definitionId", Id(exercise.ExerciseDefinitionId)), ("$mode", exercise.TrackingMode),
                ("$order", exercise.Order), ("$deletedAt", Timestamp(exercise.DeletedAt)),
                ("$version", exercise.Version));
            foreach (var set in exercise.Sets)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO LocalSet
                        (Id, OperationId, WorkoutExerciseId, SortOrder, WeightKg, AssistedKg,
                         Reps, Effort, CompletedAt, UpdatedAt, DeletedAt, Version, BaseVersion)
                    VALUES ($id, $id, $exerciseId, $order, $weight, $assisted,
                            $reps, $effort, $completedAt, $updatedAt, $deletedAt, $version, $version)
                    ON CONFLICT(Id) DO UPDATE SET
                        SortOrder = excluded.SortOrder, WeightKg = excluded.WeightKg,
                        AssistedKg = excluded.AssistedKg, Reps = excluded.Reps,
                        Effort = excluded.Effort,
                        CompletedAt = excluded.CompletedAt, UpdatedAt = excluded.UpdatedAt,
                        DeletedAt = excluded.DeletedAt, Version = excluded.Version,
                        BaseVersion = excluded.BaseVersion;
                    """, cancellationToken,
                    ("$id", Id(set.Id)), ("$exerciseId", Id(exercise.Id)), ("$order", set.Order),
                    ("$weight", set.WeightKg), ("$assisted", set.AssistedKg), ("$reps", set.Reps),
                    ("$effort", set.Effort is null ? null : (int)set.Effort.Value),
                    ("$completedAt", Timestamp(set.CompletedAt)), ("$updatedAt", Timestamp(set.UpdatedAt)),
                    ("$deletedAt", Timestamp(set.DeletedAt)), ("$version", set.Version));
            }
        }
    }

    private static async Task ValidateGraphIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncWorkoutDto graph,
        CancellationToken cancellationToken)
    {
        foreach (var exercise in graph.Exercises)
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT WorkoutId, ExerciseDefinitionId
                    FROM LocalWorkoutExercise
                    WHERE Id = $id;
                    """;
                Add(command, "$id", Id(exercise.Id));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken)
                    && (reader.GetString(0) != Id(graph.Id)
                        || reader.GetString(1) != Id(exercise.ExerciseDefinitionId)))
                    throw new InvalidDataException(
                        "The authoritative exercise identifier belongs to another graph.");
            }

            foreach (var set in exercise.Sets)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT WorkoutExerciseId
                    FROM LocalSet
                    WHERE Id = $id;
                    """;
                Add(command, "$id", Id(set.Id));
                var existingExerciseId = await command.ExecuteScalarAsync(cancellationToken);
                if (existingExerciseId is string value && value != Id(exercise.Id))
                    throw new InvalidDataException(
                        "The authoritative set identifier belongs to another exercise.");
            }
        }
    }

    private static async Task DeleteAbsentChildrenAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncWorkoutDto graph,
        CancellationToken cancellationToken)
    {
        var incomingExerciseIds = graph.Exercises.Select(item => item.Id).ToHashSet();
        var incomingSetIds = graph.Exercises.SelectMany(item => item.Sets).Select(item => item.Id).ToHashSet();
        var existingSets = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT localSet.Id
                FROM LocalSet AS localSet
                INNER JOIN LocalWorkoutExercise AS exercise
                    ON exercise.Id = localSet.WorkoutExerciseId
                WHERE exercise.WorkoutId = $workoutId;
                """;
            Add(command, "$workoutId", Id(graph.Id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                existingSets.Add(Guid.Parse(reader.GetString(0)));
        }
        foreach (var setId in existingSets.Where(id => !incomingSetIds.Contains(id)))
            await ExecuteAsync(
                connection, transaction, "DELETE FROM LocalSet WHERE Id = $id;",
                cancellationToken, ("$id", Id(setId)));

        var existingExercises = new List<Guid>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id FROM LocalWorkoutExercise WHERE WorkoutId = $workoutId;";
            Add(command, "$workoutId", Id(graph.Id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                existingExercises.Add(Guid.Parse(reader.GetString(0)));
        }
        foreach (var exerciseId in existingExercises.Where(id => !incomingExerciseIds.Contains(id)))
            await ExecuteAsync(
                connection, transaction, "DELETE FROM LocalWorkoutExercise WHERE Id = $id;",
                cancellationToken, ("$id", Id(exerciseId)));
    }

    private static void ValidateGraph(SyncWorkoutDto graph)
    {
        if (graph.Id == Guid.Empty
            || graph.Version < 1
            || !Utc(graph.StartedAt)
            || graph.Status is not (2 or 3)
            || graph.Status == 2 && graph.CompletedAt is not null
            || graph.Status == 3 && graph.CompletedAt is null
            || graph.CompletedAt is { } completion
                && (!Utc(completion) || completion < graph.StartedAt)
            || graph.DeletedAt is { } deletion
                && (!Utc(deletion) || deletion < graph.StartedAt
                    || graph.CompletedAt is { } completed && deletion < completed)
            || graph.Exercises is null
            || graph.Exercises.Count == 0
            || graph.Exercises.Select(item => item.Id).Distinct().Count() != graph.Exercises.Count)
            throw new InvalidDataException("The authoritative workout graph is malformed.");

        var activeExercises = graph.Exercises.Where(item => item.DeletedAt is null).ToArray();
        if (!Contiguous(activeExercises.Select(item => item.Order))
            || activeExercises.Select(item => item.ExerciseDefinitionId).Distinct().Count()
                != activeExercises.Length)
            throw new InvalidDataException("The authoritative exercise order is malformed.");

        var setIds = new HashSet<Guid>();
        foreach (var exercise in graph.Exercises)
        {
            if (exercise.Id == Guid.Empty
                || exercise.ExerciseDefinitionId == Guid.Empty
                || exercise.Version < 1
                || exercise.Order < 0
                || !Enum.IsDefined((TrackingMode)exercise.TrackingMode)
                || exercise.DeletedAt is { } exerciseDeletion
                    && (!Utc(exerciseDeletion) || exerciseDeletion < graph.StartedAt)
                || exercise.Sets is null)
                throw new InvalidDataException("The authoritative exercise is malformed.");
            var activeSets = exercise.Sets.Where(item => item.DeletedAt is null).ToArray();
            if (!Contiguous(activeSets.Select(item => item.Order)))
                throw new InvalidDataException("The authoritative set order is malformed.");
            if (graph.DeletedAt is { } deletedWorkoutAfterExercise
                && exercise.DeletedAt is { } deletedExerciseAfterWorkout
                && deletedWorkoutAfterExercise < deletedExerciseAfterWorkout)
                throw new InvalidDataException("The authoritative mutation chronology is malformed.");
            foreach (var set in exercise.Sets)
            {
                if (set.Id == Guid.Empty
                    || !setIds.Add(set.Id)
                    || set.Version < 1
                    || set.Order < 0
                    || set.Reps is < 1 or > 999
                    || set.Effort is { } effort && !Enum.IsDefined(effort)
                    || !Utc(set.CompletedAt)
                    || set.CompletedAt < graph.StartedAt
                    || set.UpdatedAt is { } updated
                        && (!Utc(updated) || updated < set.CompletedAt)
                    || set.DeletedAt is { } setDeletion
                        && (!Utc(setDeletion)
                            || setDeletion < (set.UpdatedAt ?? set.CompletedAt))
                    || !ValidMeasurement(
                        (TrackingMode)exercise.TrackingMode, set.WeightKg, set.AssistedKg))
                    throw new InvalidDataException("The authoritative set is malformed.");
                var lastMutation = set.DeletedAt ?? set.UpdatedAt ?? set.CompletedAt;
                if (exercise.DeletedAt is { } deletedExercise
                        && deletedExercise < lastMutation
                    || graph.CompletedAt is { } completedWorkout
                        && completedWorkout < set.CompletedAt
                    || graph.DeletedAt is { } deletedWorkout && deletedWorkout < lastMutation)
                    throw new InvalidDataException("The authoritative mutation chronology is malformed.");
            }
        }

        static bool Utc(DateTimeOffset value) =>
            value != default && value.Offset == TimeSpan.Zero;
        static bool Contiguous(IEnumerable<int> orders)
        {
            var ordered = orders.Order().ToArray();
            return ordered.Select((value, index) => value == index).All(value => value);
        }
        static bool ValidMeasurement(TrackingMode mode, string? weight, string? assisted) => mode switch
        {
            TrackingMode.Weighted => Kilograms(weight) && assisted is null,
            TrackingMode.Bodyweight => weight is null && assisted is null,
            TrackingMode.Assisted => weight is null && Kilograms(assisted),
            _ => false
        };
        static bool Kilograms(string? value)
        {
            if (value is null
                || !decimal.TryParse(
                    value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var kilograms)
                || kilograms is < 0.001m or > 99999.999m)
                return false;
            return ((decimal.GetBits(kilograms)[3] >> 16) & 0xff) <= 3;
        }
    }

    private static async Task WriteCursorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string cursor,
        DateTimeOffset now,
        CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction, """
            INSERT INTO SyncCursor (Scope, Cursor, UpdatedAt, Version)
            VALUES ($scope, $cursor, $updatedAt, 1)
            ON CONFLICT(Scope) DO UPDATE SET Cursor = excluded.Cursor,
                UpdatedAt = excluded.UpdatedAt, Version = SyncCursor.Version + 1;
            """, cancellationToken,
            ("$scope", CursorScope), ("$cursor", cursor), ("$updatedAt", Timestamp(now)));

    private static async Task<OutboxOperation> ReadOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT OperationId, EntityId, OperationType, Payload, BaseVersion,
                   CreatedAt, State, DeletedAt, Version, ServerVersion, RetryCount,
                   NextAttemptAt, ServerPayload, ReplacesOperationId
                   , SendStartedAt, NeutralizedAt, FailureCode
            FROM OutboxOperation WHERE OperationId = $id;
            """;
        Add(command, "$id", Id(operationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The operation does not exist.");
        return ReadOperation(reader);
    }

    private static OutboxOperation ReadOperation(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
        (OutboxOperationType)reader.GetInt32(2), reader.GetString(3), reader.GetInt64(4),
        DateTimeOffset.ParseExact(reader.GetString(5), "O", CultureInfo.InvariantCulture),
        (OutboxOperationState)reader.GetInt32(6),
        reader.IsDBNull(7) ? null : DateTimeOffset.ParseExact(reader.GetString(7), "O", CultureInfo.InvariantCulture),
        reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetInt64(9), reader.GetInt32(10),
        reader.IsDBNull(11) ? null : DateTimeOffset.ParseExact(reader.GetString(11), "O", CultureInfo.InvariantCulture),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)),
        reader.IsDBNull(14) ? null : DateTimeOffset.ParseExact(reader.GetString(14), "O", CultureInfo.InvariantCulture),
        reader.IsDBNull(15) ? null : DateTimeOffset.ParseExact(reader.GetString(15), "O", CultureInfo.InvariantCulture),
        reader.IsDBNull(16) ? null : (BusinessErrorCode)reader.GetInt32(16));

    private static async Task<DateTimeOffset> ReadLatestCreatedAtAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MAX(CreatedAt) FROM OutboxOperation;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text
            ? DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture)
            : DateTimeOffset.MinValue;
    }

    private static async Task<bool> HasUnrelatedLivePendingSuccessorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation original,
        IReadOnlySet<Guid> replacementChain,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT OperationId
            FROM OutboxOperation
            WHERE EntityId = $entityId AND State = 1 AND DeletedAt IS NULL
              AND (CreatedAt > $createdAt
                   OR (CreatedAt = $createdAt AND OperationId > $operationId));
            """;
        Add(command, "$entityId", Id(original.EntityId));
        Add(command, "$createdAt", Timestamp(original.CreatedAt));
        Add(command, "$operationId", Id(original.OperationId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!replacementChain.Contains(Guid.Parse(reader.GetString(0)))) return true;
        }
        return false;
    }

    private static async Task InsertOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OutboxOperation operation,
        CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction, """
            INSERT INTO OutboxOperation
                (OperationId, EntityId, OperationType, Payload, BaseVersion, CreatedAt,
                 State, DeletedAt, Version, ServerVersion, RetryCount, NextAttemptAt,
                 ServerPayload, ReplacesOperationId)
            VALUES ($id, $entityId, $type, $payload, $baseVersion, $createdAt,
                    $state, NULL, 1, NULL, 0, NULL, NULL, $replaces);
            """, cancellationToken,
            ("$id", Id(operation.OperationId)), ("$entityId", Id(operation.EntityId)),
            ("$type", (int)operation.Type), ("$payload", operation.Payload),
            ("$baseVersion", operation.BaseVersion), ("$createdAt", Timestamp(operation.CreatedAt)),
            ("$state", (int)operation.State), ("$replaces", Id(operation.ReplacesOperationId)));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) Add(command, parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static string Id(Guid value) => value.ToString("D");
    private static string? Id(Guid? value) => value?.ToString("D");
    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string? Timestamp(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
