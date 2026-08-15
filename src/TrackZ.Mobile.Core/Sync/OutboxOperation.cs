using System.Text.Json;

namespace TrackZ.Mobile.Sync;

public enum OutboxOperationType
{
    StartWorkout = 1,
    SaveSet = 2,
    CompleteWorkout = 3,
    EditSet = 4,
    DeleteSet = 5,
    DeleteWorkout = 6,
    ReorderExercises = 7
}

public enum OutboxOperationState
{
    Pending = 1,
    Applied = 2,
    Rejected = 3,
    Conflicted = 4
}

public sealed record OutboxOperation(
    Guid OperationId,
    Guid EntityId,
    OutboxOperationType Type,
    string Payload,
    long BaseVersion,
    DateTimeOffset CreatedAt,
    OutboxOperationState State = OutboxOperationState.Pending,
    DateTimeOffset? DeletedAt = null,
    long Version = 1,
    long? ServerVersion = null,
    int RetryCount = 0,
    DateTimeOffset? NextAttemptAt = null,
    string? ServerPayload = null,
    Guid? ReplacesOperationId = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OutboxOperation Create<TPayload>(
        Guid operationId,
        Guid entityId,
        OutboxOperationType type,
        TPayload payload,
        long baseVersion,
        DateTimeOffset createdAt) where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new OutboxOperation(
            operationId,
            entityId,
            type,
            JsonSerializer.Serialize(payload, JsonOptions),
            baseVersion,
            createdAt.ToUniversalTime());
    }

    public TPayload DeserializePayload<TPayload>() where TPayload : notnull =>
        JsonSerializer.Deserialize<TPayload>(Payload, JsonOptions)
        ?? throw new InvalidDataException("The outbox payload is empty or invalid.");
}

public sealed record StartWorkoutExercisePayload(
    Guid WorkoutExerciseId,
    Guid ExerciseDefinitionId,
    int TrackingMode,
    int Order);

public sealed record StartWorkoutOutboxPayload(
    Guid WorkoutId,
    DateTimeOffset StartedAt,
    IReadOnlyList<StartWorkoutExercisePayload> Exercises);

public sealed record SaveSetOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    int Order,
    string? WeightKg,
    string? AssistedKg,
    int Reps,
    DateTimeOffset CompletedAt);
