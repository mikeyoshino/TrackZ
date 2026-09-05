using TrackZ.Mobile.Features.Profile;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Coach;
using Microsoft.Data.Sqlite;

namespace TrackZ.Mobile.Tests.Profile;

public sealed class TrainingScheduleTests
{
    [Fact]
    public async Task Version_eight_upgrades_without_losing_private_data_and_plan_clears_on_signout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-schedule-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "local.db");
            var database = new TrackZLocalDatabase(path);
            var setId = Guid.NewGuid();
            await new CoachJournal(database).SetWarmupAsync(setId, true);
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE TrainingSchedule; PRAGMA user_version = 8;";
                await command.ExecuteNonQueryAsync();
            }
            var upgraded = new TrackZLocalDatabase(path);
            var store = new TrainingScheduleStore(upgraded);
            await store.SaveAsync(new DateOnly(2026, 9, 4), [DayOfWeek.Monday, DayOfWeek.Saturday, DayOfWeek.Sunday], true, 3);
            Assert.True((await new CoachJournal(upgraded).ReadAsync()).Warmups[setId]);
            var reopened = await new TrainingScheduleStore(new TrackZLocalDatabase(path)).ReadAsync();
            Assert.True(reopened.IsPlanned(new DateOnly(2026, 9, 7)));
            Assert.False(reopened.IsPlanned(new DateOnly(2026, 9, 5)));
            await upgraded.ClearPrivateDataAsync();
            Assert.Empty((await store.ReadAsync()).Revisions);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Replacing_a_plan_on_its_start_day_cannot_undo_the_goal_already_effective_this_week()
    {
        var friday = new DateOnly(2026, 9, 4);
        var monday = new DateOnly(2026, 9, 7);
        var plan = TrainingSchedule.Empty.Change(friday, [DayOfWeek.Tuesday, DayOfWeek.Thursday], true, 3);
        Assert.Equal(2, plan.GoalForWeek(monday, 3));
        plan = plan.Change(monday, [DayOfWeek.Friday], false, 2);
        Assert.Equal(2, plan.GoalForWeek(monday, 3));
        Assert.Equal(1, plan.GoalForWeek(monday.AddDays(7), 3));
    }

    [Fact]
    public void Immediate_monday_edit_still_preserves_current_week_goal_until_next_monday()
    {
        var monday = new DateOnly(2026, 9, 7);
        var plan = TrainingSchedule.Empty.Change(monday, [DayOfWeek.Monday, DayOfWeek.Friday], false, 3);
        Assert.True(plan.IsPlanned(monday));
        Assert.Equal(3, plan.GoalForWeek(monday, 3));
        Assert.Equal(2, plan.GoalForWeek(monday.AddDays(7), 3));
    }

    [Fact]
    public void Friday_change_for_next_week_preserves_this_weeks_plan_and_goal()
    {
        var monday = new DateOnly(2026, 8, 31);
        var friday = new DateOnly(2026, 9, 4);
        var plan = TrainingSchedule.Empty.Change(monday, [DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday], false, 3);
        plan = plan.Change(friday, [DayOfWeek.Monday, DayOfWeek.Saturday, DayOfWeek.Sunday], true, 3);
        Assert.True(plan.IsPlanned(friday));
        Assert.False(plan.IsPlanned(new DateOnly(2026, 9, 5)));
        Assert.True(plan.IsPlanned(new DateOnly(2026, 9, 7)));
        Assert.False(plan.IsPlanned(new DateOnly(2026, 9, 8)));
        Assert.Equal(3, plan.GoalForWeek(friday, 3));
    }

    [Fact]
    public void Immediate_change_does_not_rewrite_past_days_or_raise_this_weeks_target()
    {
        var plan = TrainingSchedule.Empty.Change(new DateOnly(2026, 8, 31),
            [DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday], false, 3);
        var friday = new DateOnly(2026, 9, 4);
        plan = plan.Change(friday, [DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday], false, 3);
        Assert.False(plan.IsPlanned(new DateOnly(2026, 8, 31)));
        Assert.True(plan.IsPlanned(new DateOnly(2026, 9, 1)));
        Assert.False(plan.IsPlanned(new DateOnly(2026, 9, 3)));
        Assert.True(plan.IsPlanned(new DateOnly(2026, 9, 5)));
        Assert.Equal(3, plan.GoalForWeek(friday, 3));
        Assert.Equal(4, plan.GoalForWeek(new DateOnly(2026, 9, 7), 3));
    }

    [Fact]
    public void Replacing_future_change_removes_superseded_plan_and_requires_at_least_one_day()
    {
        var today = new DateOnly(2026, 9, 4);
        var plan = TrainingSchedule.Empty.Change(today, [DayOfWeek.Monday], true, 3)
            .Change(today, [DayOfWeek.Tuesday, DayOfWeek.Thursday], true, 3);
        Assert.False(plan.IsPlanned(new DateOnly(2026, 9, 7)));
        Assert.True(plan.IsPlanned(new DateOnly(2026, 9, 8)));
        Assert.Single(plan.Revisions);
        Assert.Throws<ArgumentException>(() => plan.Change(today, [], false, 3));
    }
}
