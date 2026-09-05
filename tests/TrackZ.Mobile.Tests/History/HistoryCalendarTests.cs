using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.History;

public sealed class HistoryCalendarTests
{
    private static readonly DateOnly Today = new(2026, 9, 5);

    [Fact]
    public void Plan_markers_are_independent_of_completed_workouts_and_do_not_change_history_totals()
    {
        var calendar = new HistoryCalendarState(Today, TimeZoneInfo.Utc);
        var workout = Workout(4, BodyPart.Arms);
        calendar.ReplaceWorkouts([workout]);
        calendar.SetSchedule(TrackZ.Mobile.Features.Profile.TrainingSchedule.Empty.Change(Today,
            [DayOfWeek.Monday, DayOfWeek.Saturday, DayOfWeek.Sunday], false, 3));
        var saturday = Assert.Single(calendar.Days, day => day.Date == Today);
        Assert.True(saturday.IsPlanned);
        Assert.False(saturday.HasTraining);
        calendar.SelectDate(new DateOnly(2026, 9, 4));
        Assert.Equal(workout.WorkoutId, Assert.Single(calendar.SelectedWorkouts).WorkoutId);
        Assert.Equal(1, calendar.MonthWorkoutCount);
        Assert.False(Assert.Single(calendar.Days, day => day.Date == new DateOnly(2026, 9, 4)).IsPlanned);
    }

    [Fact]
    public void Month_is_monday_first_and_marks_today_selected_past_and_future_independently()
    {
        var calendar = new HistoryCalendarState(Today, TimeZoneInfo.Utc);
        Assert.Equal(35, calendar.Days.Count);
        Assert.Null(calendar.Days[0].Date);
        Assert.Equal(new DateOnly(2026, 9, 1), calendar.Days[1].Date);
        Assert.True(calendar.Days[1].IsPastWithoutTraining);
        Assert.True(calendar.Days[5].IsToday);
        Assert.True(calendar.Days[5].IsSelected);
        Assert.False(calendar.Days[6].IsPastWithoutTraining);
        Assert.False(calendar.CanNextMonth);
    }

    [Fact]
    public void Navigation_handles_leap_year_clamps_day_and_cannot_go_past_current_month()
    {
        var calendar = new HistoryCalendarState(new DateOnly(2024, 3, 31), TimeZoneInfo.Utc);
        calendar.MoveMonth(-1);
        Assert.Equal(new DateOnly(2024, 2, 29), calendar.SelectedDate);
        Assert.Equal(29, calendar.Days.Count(day => day.Date is not null));
        calendar.ShowMonth(2023, 12);
        calendar.MoveMonth(1);
        Assert.Equal(new DateOnly(2024, 1, 1), calendar.Month);
        calendar.GoToToday();
        calendar.MoveMonth(1);
        Assert.Equal(new DateOnly(2024, 3, 1), calendar.Month);
        Assert.Equal(new DateOnly(2024, 3, 31), calendar.SelectedDate);
    }

    [Fact]
    public void Filter_is_union_and_counts_full_matching_workouts_without_marking_hidden_training_as_missed()
    {
        var calendar = new HistoryCalendarState(Today, TimeZoneInfo.Utc);
        var arms = Workout(4, BodyPart.Arms);
        var legs = Workout(5, BodyPart.Legs);
        var chest = Workout(5, BodyPart.Chest);
        calendar.ReplaceWorkouts([arms, legs, chest]);
        calendar.ApplyFilter([BodyPart.Legs, BodyPart.Chest]);
        Assert.Equal(2, calendar.MonthWorkoutCount);
        Assert.Equal(2, calendar.MonthSetCount);
        Assert.Equal(2, calendar.SelectedWorkouts.Count);
        var hidden = Assert.Single(calendar.Days, day => day.Date == new DateOnly(2026, 9, 4));
        Assert.True(hidden.HasTraining);
        Assert.False(hidden.HasMatchingTraining);
        Assert.False(hidden.IsPastWithoutTraining);
        calendar.SelectDate(new DateOnly(2026, 9, 4));
        Assert.Empty(calendar.SelectedWorkouts);
        calendar.ApplyFilter([]);
        Assert.Equal(arms.WorkoutId, Assert.Single(calendar.SelectedWorkouts).WorkoutId);
    }

    [Fact]
    public void Uses_local_calendar_date_and_excludes_deleted_workouts_sets_and_empty_exercises()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test Bangkok", TimeSpan.FromHours(7), "Bangkok", "Bangkok");
        var calendar = new HistoryCalendarState(new DateOnly(2026, 10, 1), zone);
        var workout = Workout(5, BodyPart.Arms) with { CompletedAt = new DateTimeOffset(2026, 9, 30, 19, 0, 0, TimeSpan.Zero) };
        var deleted = workout with { WorkoutId = Guid.NewGuid(), IsDeleted = true };
        calendar.ReplaceWorkouts([workout, deleted]);
        Assert.Equal(1, calendar.MonthWorkoutCount);
        Assert.Single(calendar.SelectedWorkouts);
        var removedSet = Workout(5, BodyPart.Arms, true) with { CompletedAt = workout.CompletedAt };
        calendar.ReplaceWorkouts([removedSet, deleted]);
        Assert.Equal(0, calendar.MonthWorkoutCount);
        Assert.Equal(0, calendar.MonthSetCount);
        Assert.Empty(calendar.SelectedWorkouts);
    }

    [Fact]
    public void Refresh_keeps_selection_and_filter_while_reset_removes_account_data_and_filters()
    {
        var calendar = new HistoryCalendarState(Today, TimeZoneInfo.Utc);
        calendar.ApplyFilter([BodyPart.Arms]);
        calendar.SelectDate(new DateOnly(2026, 9, 4));
        calendar.ReplaceWorkouts([Workout(4, BodyPart.Arms)]);
        Assert.Single(calendar.SelectedWorkouts);
        Assert.Equal(new DateOnly(2026, 9, 4), calendar.SelectedDate);
        calendar.Reset(Today);
        Assert.Empty(calendar.SelectedWorkouts);
        Assert.Empty(calendar.Filter);
        Assert.DoesNotContain(calendar.Days, day => day.HasTraining);
    }

    private static HistoryWorkoutItem Workout(int day, BodyPart bodyPart, bool deletedSet = false)
    {
        var workoutId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 9, day, 10, 0, 0, TimeSpan.Zero);
        var localSet = new LocalSet(10, null, 8) { CompletedAt = at, DeletedAt = deletedSet ? at : null };
        var localExercise = new LocalWorkoutExercise(exerciseId, workoutId, Guid.NewGuid(), TrackingMode.Weighted,
            0, null, 1, 0, [localSet]);
        var set = HistorySetItem.From(workoutId, false, false, localExercise, localSet, null, WorkoutResources.English);
        var exercise = new HistoryExerciseItem(workoutId, exerciseId, Guid.NewGuid(), "Test exercise",
            TrackingMode.Weighted, false, false, [set], bodyPart);
        return new HistoryWorkoutItem(workoutId, set.CompletedAt, false, [exercise],
            WorkoutSyncState.Synced, "Synced", null, null, null);
    }
}
