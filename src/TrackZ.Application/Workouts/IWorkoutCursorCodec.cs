namespace TrackZ.Application.Workouts;

public interface IWorkoutCursorCodec
{
    WorkoutCursor Decode(string cursor);

    string Encode(WorkoutCursor cursor);
}

public sealed record WorkoutCursor(int Version, DateTimeOffset CompletedAt, Guid WorkoutId);
