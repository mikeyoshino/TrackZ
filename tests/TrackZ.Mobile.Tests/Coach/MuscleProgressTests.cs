using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Data;

namespace TrackZ.Mobile.Tests.Coach;

public sealed class MuscleProgressTests
{
    private static readonly DateOnly End = new(2026, 9, 6);
    private static readonly Guid Id = Guid.NewGuid();
    private static CoachSession Session(int days, int reps, decimal weight = 40, int sets = 3) =>
        new(Guid.NewGuid(), Id, new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero).AddDays(-days),
            TrackingMode.Weighted, weight, null, reps, sets, 0, null, false, false);
    private static CoachExercise Exercise(params CoachSession[] sessions) =>
        new(Id, "Bench press", BodyPart.Chest, TrackingMode.Weighted, sessions, new(CoachAction.Hold), false);
    private static MuscleProgressReport Build(params CoachExercise[] exercises) =>
        MuscleProgressReport.Build(new CoachReport(End, [], [], exercises, 0), End.AddDays(-27), End, TimeZoneInfo.Utc);

    [Fact]
    public void Same_load_and_set_count_compares_reps_in_chronological_order()
    {
        var result = Build(Exercise(Session(4, 10), Session(25, 8), Session(11, 9), Session(18, 9)));
        var chest = result.Areas.Single(a => a.BodyPart == BodyPart.Chest);
        var item = Assert.Single(chest.Exercises);
        Assert.Equal(ProgressChange.MoreReps, item.Change);
        Assert.Equal(8, item.Before!.Reps);
        Assert.Equal(10, item.Latest!.Reps);
        Assert.Equal(new[] { 8, 9, 9, 10 }, item.Trend.Select(s => s.Reps));
        Assert.Equal(1, result.ImprovedAreas);
        Assert.Equal(new[] { 3, 3, 3, 3 }, chest.Weeks.Select(w => w.Sets));
        Assert.Equal(6, result.Areas.Count);
    }

    [Theory]
    [InlineData(45, 8, ProgressChange.MoreWeight)]
    [InlineData(45, 6, ProgressChange.NotComparable)]
    [InlineData(40, 8, ProgressChange.Similar)]
    [InlineData(40, 6, ProgressChange.FewerReps)]
    public void Heavier_with_fewer_reps_is_not_automatically_an_improvement(decimal kg, int reps, ProgressChange expected)
    {
        var item = Assert.Single(Build(Exercise(Session(20, 8), Session(2, reps, kg))).Areas[0].Exercises);
        Assert.Equal(expected, item.Change);
    }

    [Fact]
    public void Unknown_latest_and_changed_set_count_do_not_reuse_an_old_positive_result()
    {
        var history = new[] { Session(20, 8), Session(10, 10), Session(1, 12) with { HasUnknownSets = true } };
        Assert.Equal(ProgressChange.NotComparable, Build(Exercise(history)).Areas[0].Exercises[0].Change);
        Assert.Equal(ProgressChange.NotComparable, Build(Exercise(Session(20, 8), Session(1, 10, sets: 4))).Areas[0].Exercises[0].Change);
    }

    [Fact]
    public void Range_filters_local_dates_and_does_not_compare_outside_the_range()
    {
        var result = Build(Exercise(Session(28, 8), Session(0, 10), Session(-1, 12)));
        var item = result.Areas[0].Exercises[0];
        Assert.Single(item.Sessions);
        Assert.Equal(ProgressChange.NotComparable, item.Change);
        var late = Session(0, 10) with { At = new DateTimeOffset(2026, 9, 6, 23, 30, 0, TimeSpan.Zero) };
        var report = new CoachReport(End, [], [], [Exercise(late)], 0);
        var thai = MuscleProgressReport.Build(report, End, End, TimeZoneInfo.CreateCustomTimeZone("TH", TimeSpan.FromHours(7), "TH", "TH"));
        Assert.Empty(thai.Areas[0].Exercises);
    }

    [Fact]
    public void Assisted_progress_means_less_assistance_not_more()
    {
        var first = Session(15, 8) with { Mode = TrackingMode.Assisted, WeightKg = null, AssistedKg = 30 };
        var last = Session(1, 8) with { Mode = TrackingMode.Assisted, WeightKg = null, AssistedKg = 25 };
        var exercise = Exercise(first, last) with { Mode = TrackingMode.Assisted };
        Assert.Equal(ProgressChange.LessAssistance, Build(exercise).Areas[0].Exercises[0].Change);
    }

    [Fact]
    public void Active_workout_is_not_compared_to_completed_workouts()
    {
        var active = Session(0, 12) with { IsCompleted = false };
        Assert.Equal(ProgressChange.NotComparable, Build(Exercise(Session(5, 8), active)).Areas[0].Exercises[0].Change);
    }

    [Fact]
    public async Task Form_check_in_survives_reload_without_clearing_pain_or_inventing_effort()
    {
        var path = Path.Combine(Path.GetTempPath(), $"progress-checkin-{Guid.NewGuid():N}.db");
        try
        {
            var journal = new CoachJournal(new TrackZLocalDatabase(path));
            var session = Session(1, 10) with { LastSetId = Guid.NewGuid(), Pain = true, Effort = 3 };
            await journal.SaveControlAsync(session, true, session.At.AddHours(1));
            var saved = Assert.Single((await new CoachJournal(new TrackZLocalDatabase(path)).ReadAsync()).Assessments);
            Assert.True(saved.Controlled);
            Assert.True(saved.Pain);
            Assert.Equal(3, saved.Effort);
            Assert.False(saved.Accepted);
            await journal.SaveControlAsync(session, false, session.At.AddHours(2));
            Assert.False(Assert.Single((await journal.ReadAsync()).Assessments).Controlled);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }
}
