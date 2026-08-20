namespace TrackZ.Domain.Gamification;

public sealed record CompletedWorkoutInstant(Guid WorkoutId, DateTimeOffset CompletedAtUtc);

public sealed record WeeklyGoalEvaluation(
    YearWeek CurrentWeek,
    int CurrentWeekCompletedWorkouts,
    bool CurrentWeekGoalMet,
    IReadOnlyDictionary<YearWeek, int> CompletedWorkoutCounts);

public sealed record StreakRebuildResult(
    int CurrentWeeks,
    int BestWeeks,
    YearWeek LastEvaluatedWeek,
    IReadOnlyList<YearWeek> GoalMetWeeks);

public static class WeeklyGoalCalculator
{
    public static StreakRebuildResult RebuildStreak(
        int goal,
        string timeZoneId,
        IEnumerable<CompletedWorkoutInstant> completedWorkouts,
        DateTimeOffset currentInstantUtc)
    {
        var evaluation = Evaluate(goal, timeZoneId, completedWorkouts, currentInstantUtc);
        var metWeeks = evaluation.CompletedWorkoutCounts
            .Where(pair => pair.Value >= goal)
            .Select(pair => pair.Key)
            .Order()
            .ToArray();
        var lastEvaluated = evaluation.CurrentWeekGoalMet
            ? evaluation.CurrentWeek
            : evaluation.CurrentWeek.Previous();
        if (evaluation.CompletedWorkoutCounts.Count == 0)
        {
            return new StreakRebuildResult(0, 0, lastEvaluated, metWeeks);
        }

        var first = evaluation.CompletedWorkoutCounts.Keys.Min();
        var current = 0;
        var best = 0;
        for (var week = first; week.CompareTo(lastEvaluated) <= 0; week = week.Next())
        {
            current = evaluation.CompletedWorkoutCounts.GetValueOrDefault(week) >= goal
                ? current + 1
                : 0;
            best = Math.Max(best, current);
        }

        return new StreakRebuildResult(current, best, lastEvaluated, metWeeks);
    }

    public static WeeklyGoalEvaluation Evaluate(
        int goal,
        string timeZoneId,
        IEnumerable<CompletedWorkoutInstant> completedWorkouts,
        DateTimeOffset currentInstantUtc)
    {
        if (goal is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(goal));
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ArgumentNullException.ThrowIfNull(completedWorkouts);
        if (currentInstantUtc == default) throw new ArgumentOutOfRangeException(nameof(currentInstantUtc));

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var currentWeek = ToYearWeek(currentInstantUtc, timeZone);
        var counts = completedWorkouts
            .Where(completion => completion.WorkoutId != Guid.Empty && completion.CompletedAtUtc != default)
            .GroupBy(completion => ToYearWeek(completion.CompletedAtUtc, timeZone))
            .ToDictionary(
                group => group.Key,
                group => group.Select(completion => completion.WorkoutId).Distinct().Count());
        var currentCount = counts.GetValueOrDefault(currentWeek);
        return new WeeklyGoalEvaluation(
            currentWeek,
            currentCount,
            currentCount >= goal,
            counts);
    }

    private static YearWeek ToYearWeek(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(instant.UtcDateTime, timeZone);
        return YearWeek.FromLocalDate(DateOnly.FromDateTime(local));
    }
}
