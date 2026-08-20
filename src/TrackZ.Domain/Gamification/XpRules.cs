namespace TrackZ.Domain.Gamification;

public static class XpRules
{
    public const int CompletedWorkoutXp = 100;
    public const int PerValidSetXp = 5;
    public const int MaximumSetXpPerWorkout = 100;
    public const int WeeklyGoalXp = 150;

    public static int ForCompletedWorkout(int validSetCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(validSetCount);
        return CompletedWorkoutXp
            + Math.Min(validSetCount * PerValidSetXp, MaximumSetXpPerWorkout);
    }
}
