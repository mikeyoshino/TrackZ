namespace TrackZ.Application.Workouts;

public interface IWorkoutCursorCodec
{
    WorkoutCursor Decode(string cursor, WorkoutCursorScope expectedScope);

    string Encode(WorkoutCursorScope scope, DateTimeOffset completedAt, Guid workoutId);
}

public enum WorkoutCursorPurpose
{
    WorkoutHistory = 1,
    ExerciseHistory = 2
}

public sealed record WorkoutCursorScope(
    Guid OwnerId,
    WorkoutCursorPurpose Purpose,
    Guid? ExerciseId);

public sealed record WorkoutCursor(
    int Version,
    WorkoutCursorPurpose Purpose,
    Guid OwnerId,
    Guid? ExerciseId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CompletedAt,
    Guid WorkoutId);
