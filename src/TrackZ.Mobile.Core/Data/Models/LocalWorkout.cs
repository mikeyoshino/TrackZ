namespace TrackZ.Mobile.Data.Models;

public enum LocalWorkoutStatus
{
    Draft = 1,
    Active = 2,
    Completed = 3
}

public sealed record LocalWorkout(
    Guid Id,
    LocalWorkoutStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeletedAt,
    long Version,
    long BaseVersion,
    IReadOnlyList<LocalWorkoutExercise> Exercises);
