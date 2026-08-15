namespace TrackZ.Infrastructure.Workouts;

public sealed class WorkoutCursorOptions
{
    public const string SectionName = "WorkoutCursor";

    public int LifetimeMinutes { get; init; } = 60;

    public bool IsValid() => LifetimeMinutes is >= 5 and <= 1440;
}
