using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;

namespace TrackZ.Mobile.Features.Progress;

public enum ProgressChange { NotComparable, Similar, MoreReps, MoreWeight, LessAssistance, FewerReps }
public sealed record ProgressWeek(DateOnly Start, DateOnly End, int Sets, int UnclassifiedSets);
public sealed record ExerciseProgressEvidence(CoachExercise Exercise, IReadOnlyList<CoachSession> Sessions,
    CoachSession? Before, CoachSession? Latest, ProgressChange Change, IReadOnlyList<CoachSession> Trend)
{
    public bool Improved => Change is ProgressChange.MoreReps or ProgressChange.MoreWeight or ProgressChange.LessAssistance;
}
public sealed record MuscleProgressArea(BodyPart BodyPart, IReadOnlyList<ExerciseProgressEvidence> Exercises,
    IReadOnlyList<ProgressWeek> Weeks)
{
    public int Improved => Exercises.Count(e => e.Improved);
    public int Comparable => Exercises.Count(e => e.Change != ProgressChange.NotComparable);
}
public sealed record MuscleProgressReport(DateOnly Start, DateOnly End, IReadOnlyList<MuscleProgressArea> Areas)
{
    public int ImprovedAreas => Areas.Count(a => a.Improved > 0);

    public static MuscleProgressReport Build(CoachReport source, DateOnly start, DateOnly end, TimeZoneInfo zone)
    {
        if (end < start) throw new ArgumentException("End must not precede start.");
        DateOnly Date(CoachSession s) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.At, zone).DateTime);
        var exercises = source.Exercises.Select(e => Evaluate(e, e.Sessions
            .Where(s => s.IsCompleted && Date(s) >= start && Date(s) <= end)
            .OrderBy(s => s.At).DistinctBy(s => s.WorkoutId).ToArray()))
            .Where(e => e.Sessions.Count > 0).ToArray();
        return new(start, end, Enum.GetValues<BodyPart>().Select(body =>
        {
            var items = exercises.Where(e => e.Exercise.BodyPart == body).OrderBy(e => e.Exercise.Name).ToArray();
            var weeks = new List<ProgressWeek>();
            for (var day = start; day <= end; day = day.AddDays(7))
            {
                var last = day.AddDays(Math.Min(6, end.DayNumber - day.DayNumber));
                var sessions = items.SelectMany(e => e.Sessions).Where(s => Date(s) >= day && Date(s) <= last).ToArray();
                weeks.Add(new(day, last, sessions.Sum(s => s.WorkingSets), sessions.Sum(s => s.UnclassifiedSets)));
            }
            return new MuscleProgressArea(body, items, weeks);
        }).ToArray());
    }

    private static bool Valid(CoachSession s) => !s.HasUnknownSets && s.WorkingSets > 0 && s.Reps > 0
        && (s.Mode != TrackingMode.Weighted || s.WeightKg is > 0)
        && (s.Mode != TrackingMode.Assisted || s.AssistedKg is >= 0);
    private static bool SameLoad(CoachSession a, CoachSession b) => a.WeightKg == b.WeightKg && a.AssistedKg == b.AssistedKg;

    private static ExerciseProgressEvidence Evaluate(CoachExercise exercise, CoachSession[] sessions)
    {
        var latest = sessions.LastOrDefault();
        if (latest is null || !Valid(latest))
            return new(exercise, sessions, null, latest, ProgressChange.NotComparable, []);
        var candidates = sessions.Where(s => Valid(s) && s.Mode == latest.Mode && s.WorkingSets == latest.WorkingSets).ToArray();
        var trend = candidates.Where(s => SameLoad(s, latest)).ToArray();
        // Compare the earliest comparable observation with the latest, never two unrelated exercises.
        var before = candidates.FirstOrDefault(s => s.WorkoutId != latest.WorkoutId &&
            (SameLoad(s, latest) || latest.Reps >= s.Reps &&
                (latest.Mode == TrackingMode.Weighted && latest.WeightKg > s.WeightKg
                 || latest.Mode == TrackingMode.Assisted && latest.AssistedKg < s.AssistedKg)));
        var change = before is null ? ProgressChange.NotComparable
            : SameLoad(before, latest) ? latest.Reps.CompareTo(before.Reps) switch
                { > 0 => ProgressChange.MoreReps, < 0 => ProgressChange.FewerReps, _ => ProgressChange.Similar }
            : latest.Mode == TrackingMode.Assisted ? ProgressChange.LessAssistance : ProgressChange.MoreWeight;
        return new(exercise, sessions, before, latest, change, trend);
    }
}
