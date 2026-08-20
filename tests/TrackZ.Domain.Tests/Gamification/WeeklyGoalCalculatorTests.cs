using TrackZ.Domain.Gamification;

namespace TrackZ.Domain.Tests.Gamification;

public sealed class WeeklyGoalCalculatorTests
{
    [Fact]
    public void Workout_is_counted_in_the_users_local_iso_week()
    {
        var result = WeeklyGoalCalculator.Evaluate(
            goal: 2,
            timeZoneId: "Asia/Bangkok",
            completedWorkouts:
            [
                Completion("10000000-0000-0000-0000-000000000000", "2026-08-09T18:30:00Z"),
                Completion("20000000-0000-0000-0000-000000000000", "2026-08-14T10:00:00Z")
            ],
            currentInstantUtc: DateTimeOffset.Parse("2026-08-14T12:00:00Z"));

        Assert.True(result.CurrentWeekGoalMet);
        Assert.Equal(new YearWeek(2026, 33), result.CurrentWeek);
        Assert.Equal(2, result.CurrentWeekCompletedWorkouts);
    }

    [Fact]
    public void Duplicate_workout_ids_count_once()
    {
        var workoutId = "10000000-0000-0000-0000-000000000000";
        var result = WeeklyGoalCalculator.Evaluate(
            2,
            "Asia/Bangkok",
            [Completion(workoutId, "2026-08-11T10:00:00Z"), Completion(workoutId, "2026-08-12T10:00:00Z")],
            DateTimeOffset.Parse("2026-08-14T12:00:00Z"));

        Assert.False(result.CurrentWeekGoalMet);
        Assert.Equal(1, result.CurrentWeekCompletedWorkouts);
    }

    [Fact]
    public void Missed_week_resets_current_streak_but_preserves_best()
    {
        var state = StreakState.Create(Guid.NewGuid());
        state.Recalculate(
            currentWeeks: 4,
            bestWeeks: 6,
            lastEvaluatedWeek: new YearWeek(2026, 32),
            updatedAt: DateTimeOffset.Parse("2026-08-10T00:00:00Z"));

        state.ApplyWeek(
            goalMet: false,
            week: new YearWeek(2026, 33),
            updatedAt: DateTimeOffset.Parse("2026-08-17T00:00:00Z"));

        Assert.Equal(0, state.CurrentWeeks);
        Assert.Equal(6, state.BestWeeks);
    }

    [Fact]
    public void Rebuild_does_not_treat_the_incomplete_current_week_as_missed()
    {
        var result = WeeklyGoalCalculator.RebuildStreak(
            goal: 1,
            timeZoneId: "Asia/Bangkok",
            completedWorkouts:
            [
                Completion("10000000-0000-0000-0000-000000000000", "2026-07-21T10:00:00Z"),
                Completion("20000000-0000-0000-0000-000000000000", "2026-07-28T10:00:00Z"),
                Completion("30000000-0000-0000-0000-000000000000", "2026-08-11T10:00:00Z")
            ],
            currentInstantUtc: DateTimeOffset.Parse("2026-08-18T10:00:00Z"));

        Assert.Equal(1, result.CurrentWeeks);
        Assert.Equal(2, result.BestWeeks);
        Assert.Equal(new YearWeek(2026, 33), result.LastEvaluatedWeek);
        Assert.Equal([new YearWeek(2026, 30), new YearWeek(2026, 31), new YearWeek(2026, 33)], result.GoalMetWeeks);
    }

    private static CompletedWorkoutInstant Completion(string id, string completedAtUtc) =>
        new(Guid.Parse(id), DateTimeOffset.Parse(completedAtUtc));
}
