using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Tests.Coach;

public sealed class CoachJournalAndReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ExerciseId = Guid.Parse("09e37118-df35-4b68-925c-5e05ed498722");

    [Fact]
    public async Task Journal_survives_restart_and_is_erased_with_private_workouts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trackz-coach-{Guid.NewGuid():N}.db");
        try
        {
            var db = new TrackZLocalDatabase(path);
            var journal = new CoachJournal(db);
            var assessment = new CoachAssessment(Guid.NewGuid(), ExerciseId, Now, 1, true, false, true, Guid.NewGuid());
            await journal.SaveAssessmentAsync(assessment);
            await journal.SetWarmupAsync(assessment.LastSetId, false);
            var restored = await new CoachJournal(new TrackZLocalDatabase(path)).ReadAsync();
            Assert.Equal(assessment, Assert.Single(restored.Assessments));
            Assert.False(restored.Warmups[assessment.LastSetId]);
            await db.ClearPrivateDataAsync();
            var cleared = await journal.ReadAsync();
            Assert.Empty(cleared.Assessments);
            Assert.Empty(cleared.Warmups);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }

    [Fact]
    public void Report_deduplicates_days_and_excludes_warmups_and_deleted_sets()
    {
        var first = Workout(Now.AddHours(-2));
        var second = Workout(Now.AddHours(-1));
        var journal = CoachJournalData.Empty();
        journal.Warmups[first.Exercises[0].Sets[0].Id] = false;
        journal.Warmups[second.Exercises[0].Sets[0].Id] = true;
        var report = Report([first, second], journal);
        Assert.Equal(1, report.TrainingDays);
        Assert.Equal(1, report.WorkingSets);
        Assert.Equal(0, report.UnknownSets);
        Assert.Equal(6, report.Areas.Count);
        Assert.Equal(1, report.Areas.Single(a => a.BodyPart == BodyPart.Arms).WorkingSets);
        Assert.Equal(0, Report([first with { DeletedAt = Now }], journal).WorkingSets);
    }

    [Fact]
    public void Legacy_unclassified_sets_are_not_misrepresented_as_working_sets()
    {
        var report = Report([Workout(Now.AddHours(-1))], CoachJournalData.Empty());
        Assert.Equal(0, report.WorkingSets);
        Assert.Equal(1, report.UnknownSets);
        Assert.False(Assert.Single(report.Exercises).Recommendation.IsIncrease);
    }

    [Fact]
    public void New_or_edited_sets_invalidate_old_form_assessment()
    {
        var first = Workout(Now.AddDays(-4));
        var last = Workout(Now.AddHours(-1));
        var data = CoachJournalData.Empty();
        foreach (var workout in new[] { first, last })
        {
            var set = workout.Exercises[0].Sets[0];
            data.Warmups[set.Id] = false;
            data.Assessments.Add(new(workout.Id, ExerciseId, set.CompletedAt.AddMinutes(1), 1, true, false, true, set.Id));
        }
        Assert.True(Assert.Single(Report([first, last], data).Exercises).Recommendation.IsIncrease);
        last = last with { Exercises = [last.Exercises[0] with { Sets = [last.Exercises[0].Sets[0] with { UpdatedAt = Now }] }] };
        Assert.False(Assert.Single(Report([first, last], data).Exercises).Recommendation.IsIncrease);
    }

    private static CoachReport Report(IReadOnlyList<LocalWorkout> workouts, CoachJournalData journal) =>
        TrainingCoachSource.BuildReport(workouts, [new CachedExercise { Id = ExerciseId, Name = "Curl", BodyPart = BodyPart.Arms, TrackingMode = TrackingMode.Weighted }], journal, Now, TimeZoneInfo.Utc, _ => null);

    private static LocalWorkout Workout(DateTimeOffset at)
    {
        var workoutId = Guid.NewGuid(); var workoutExerciseId = Guid.NewGuid();
        return new(workoutId, LocalWorkoutStatus.Completed, at.AddHours(-1), at, null, 1, 0,
            [new(workoutExerciseId, workoutId, ExerciseId, TrackingMode.Weighted, 0, null, 1, 0,
                [new LocalSet(5m, null, 10) with { WorkoutExerciseId = workoutExerciseId, CompletedAt = at }])]);
    }
}
