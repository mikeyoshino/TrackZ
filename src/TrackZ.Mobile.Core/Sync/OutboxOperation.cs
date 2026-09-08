using System.Text.Json;
using System.Text.Json.Serialization;
using TrackZ.Contracts.Errors;

namespace TrackZ.Mobile.Sync;

public enum OutboxOperationType
{
    StartWorkout = 1,
    SaveSet = 2,
    CompleteWorkout = 3,
    EditSet = 4,
    DeleteSet = 5,
    DeleteWorkout = 6,
    ReorderExercises = 7,
    AddExercise = 8,
    RemoveExercise = 9,
    DeleteWorkoutExercise = 10,
    RecordSetEffort = 11
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
    Guid? ReplacesOperationId = null,
    DateTimeOffset? SendStartedAt = null,
    DateTimeOffset? NeutralizedAt = null,
    BusinessErrorCode? FailureCode = null)
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
    DateTimeOffset CompletedAt,
    int? PlateCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EffortScore = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsWarmup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HasPain = null);

public sealed record RecordSetEffortOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    int Effort,
    DateTimeOffset RecordedAt);

public sealed record CompleteWorkoutOutboxPayload(
    Guid WorkoutId,
    DateTimeOffset CompletedAt);

public sealed record EditSetOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    string? WeightKg,
    string? AssistedKg,
    int Reps,
    DateTimeOffset UpdatedAt,
    int? PlateCount = null,
    bool CoachingMetadataSpecified = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? EffortScore = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsWarmup = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? HasPain = null);

public sealed record DeleteSetOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid SetId,
    DateTimeOffset DeletedAt);

public sealed record DeleteWorkoutOutboxPayload(
    Guid WorkoutId,
    DateTimeOffset DeletedAt);

public sealed record DeleteWorkoutExerciseOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    DateTimeOffset DeletedAt);

public sealed record AddExerciseOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    Guid ExerciseDefinitionId,
    int TrackingMode,
    int Order,
    DateTimeOffset AddedAt);

public sealed record RemoveExerciseOutboxPayload(
    Guid WorkoutId,
    Guid WorkoutExerciseId,
    DateTimeOffset DeletedAt);

public sealed record ReorderExercisesOutboxPayload(
    Guid WorkoutId,
    IReadOnlyList<Guid> WorkoutExerciseIds,
    DateTimeOffset ReorderedAt);
