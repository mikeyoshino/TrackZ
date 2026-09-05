using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Profile;

namespace TrackZ.Mobile.Features.Coach;

public sealed record CoachDay(DateOnly Date, bool Trained, bool IsToday, bool IsPlanned = false);
public sealed record CoachArea(BodyPart BodyPart, int WorkingSets, int UnclassifiedSets, bool NeedsCheck, CoachRecovery? Recovery);
public sealed record CoachExercise(Guid Id, string Name, BodyPart BodyPart, TrackingMode Mode,
    IReadOnlyList<CoachSession> Sessions, CoachRecommendation Recommendation, bool Accepted);
public sealed record CoachReport(DateOnly Week, IReadOnlyList<CoachDay> Days,
    IReadOnlyList<CoachArea> Areas, IReadOnlyList<CoachExercise> Exercises, int UnknownSets)
{
    public int TrainingDays => Days.Count(d => d.Trained);
    public int WorkingSets => Areas.Sum(a => a.WorkingSets);
    public bool HasTraining => TrainingDays > 0;
}

public sealed class TrainingCoachSource(
    ILocalWorkoutRepository workouts, ExerciseCache exercises, CoachJournal journal,
    IExerciseGuidancePreferenceStore preferences, IClock clock, TimeZoneInfo timeZone, TrainingScheduleStore? schedules = null)
{
    public async Task<CoachReport> LoadAsync(CancellationToken token = default)
    {
        var history = await workouts.GetHistoryAsync(token);
        var active = await workouts.GetActiveAsync(token);
        var definitions = await exercises.GetAllAsync(token);
        var data = await journal.ReadAsync(token);
        var all = history.Concat(active is null ? [] : new[] { active }).DistinctBy(w => w.Id)
            .Where(w => w.DeletedAt is null).ToArray();
        var report = BuildReport(all, definitions, data, clock.UtcNow, timeZone, preferences.GetIncrementKg);
        if (schedules is null) return report;
        var plan = await schedules.ReadAsync(token);
        return report with { Days = report.Days.Select(day => day with { IsPlanned = plan.IsPlanned(day.Date) }).ToArray() };
    }

    public async Task<(LocalWorkout Workout, LocalWorkoutExercise Exercise, CachedExercise Definition)?> GetActiveExerciseAsync(
        Guid exerciseId, CancellationToken token = default)
    {
        var workout = await workouts.GetActiveAsync(token);
        var exercise = workout?.Exercises.FirstOrDefault(e => e.DeletedAt is null && e.ExerciseDefinitionId == exerciseId);
        var definition = (await exercises.GetAllAsync(token)).FirstOrDefault(e => e.Id == exerciseId);
        return workout is not null && exercise is not null && definition is not null ? (workout, exercise, definition) : null;
    }

    public static CoachReport BuildReport(IReadOnlyList<LocalWorkout> workouts, IReadOnlyList<CachedExercise> definitions,
        CoachJournalData journal, DateTimeOffset now, TimeZoneInfo zone, Func<Guid, decimal?> increment)
    {
        DateOnly LocalDate(DateTimeOffset date) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date, zone).DateTime);
        var today = LocalDate(now);
        var week = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var live = workouts.Where(w => w.DeletedAt is null && w.StartedAt <= now).ToArray();
        var completed = live.Where(w => w.Status == LocalWorkoutStatus.Completed && w.CompletedAt <= now).ToArray();
        var trained = completed.Where(w => w.Exercises.Any(e => e.DeletedAt is null && e.Sets.Any(s => s.DeletedAt is null)))
            .Select(w => LocalDate(w.CompletedAt!.Value)).ToHashSet();
        var days = Enumerable.Range(0, 7).Select(i => new CoachDay(week.AddDays(i), trained.Contains(week.AddDays(i)), week.AddDays(i) == today)).ToArray();
        var defs = definitions.ToDictionary(d => d.Id);
        var rows = live.SelectMany(w => w.Exercises.Where(e => e.DeletedAt is null).SelectMany(e => e.Sets
            .Where(s => s.DeletedAt is null && s.CompletedAt <= now)
            .Select(s => (Exercise: e, Set: s, Date: LocalDate(s.CompletedAt), Definition: defs.GetValueOrDefault(e.ExerciseDefinitionId)))))
            .ToArray();
        var areas = Enum.GetValues<BodyPart>().Select(body =>
        {
            var bodyRows = rows.Where(r => r.Definition?.BodyPart == body).ToArray();
            int Count(DateOnly start) => bodyRows.Count(r => r.Date >= start && r.Date < start.AddDays(7)
                && journal.Warmups.TryGetValue(r.Set.Id, out var warmup) && !warmup);
            var count = Count(week);
            var baseline = Enumerable.Range(1, 3).Select(i => Count(week.AddDays(-i * 7))).ToArray();
            var unknown = bodyRows.Count(r => r.Date >= week && r.Date <= today && !journal.Warmups.ContainsKey(r.Set.Id));
            var baselineUnknown = bodyRows.Any(r => r.Date >= week.AddDays(-21) && r.Date < week && !journal.Warmups.ContainsKey(r.Set.Id));
            return new CoachArea(body, count, unknown,
                !baselineUnknown && TrainingCoachPolicy.NeedsRecoveryCheck(count, baseline),
                journal.Recovery.LastOrDefault(r => r.BodyPart == body && r.Week == week));
        }).ToArray();
        var items = definitions.Select(definition =>
        {
            var sessions = live.SelectMany(w => w.Exercises.Where(e => e.DeletedAt is null && e.ExerciseDefinitionId == definition.Id)
                .Select(e => ToSession(w, e, journal, now))).Where(s => s is not null).Cast<CoachSession>()
                .OrderByDescending(s => s.At).ToArray();
            if (sessions.Length == 0) return null;
            var recommendation = TrainingCoachPolicy.Evaluate(sessions, now, increment(definition.Id));
            var area = areas.Single(a => a.BodyPart == definition.BodyPart);
            if (recommendation.Action != CoachAction.Pain && (area.Recovery is { Ready: false } || area.NeedsCheck && area.Recovery is null))
                recommendation = new(CoachAction.CheckRecovery);
            var latest = sessions[0];
            var assessment = journal.Assessments.LastOrDefault(a => a.WorkoutId == latest.WorkoutId && a.ExerciseId == definition.Id);
            return new CoachExercise(definition.Id, definition.Name, definition.BodyPart, definition.TrackingMode,
                sessions, recommendation, assessment?.Accepted == true && recommendation.IsIncrease);
        }).Where(e => e is not null).Cast<CoachExercise>().OrderByDescending(e => e.Sessions[0].At).ToArray();
        return new CoachReport(week, days, areas, items, rows.Count(r => r.Date >= week && r.Date <= today && !journal.Warmups.ContainsKey(r.Set.Id)));
    }

    private static CoachSession? ToSession(LocalWorkout workout, LocalWorkoutExercise exercise, CoachJournalData data, DateTimeOffset now)
    {
        var sets = exercise.Sets.Where(s => s.DeletedAt is null && s.CompletedAt <= now).OrderBy(s => s.Order).ToArray();
        if (sets.Length == 0) return null;
        var working = sets.Where(s => data.Warmups.TryGetValue(s.Id, out var warmup) && !warmup).ToArray();
        var last = working.LastOrDefault() ?? sets[^1];
        var assessment = data.Assessments.LastOrDefault(a => a.WorkoutId == workout.Id && a.ExerciseId == exercise.ExerciseDefinitionId
            && a.LastSetId == sets[^1].Id && a.At >= sets.Max(s => s.UpdatedAt ?? s.CompletedAt) && a.At <= now);
        var unknown = sets.Any(s => !data.Warmups.ContainsKey(s.Id)) || working.Any(s => s.PlateCount is not null
            || s.WeightKg != last.WeightKg || s.AssistedKg != last.AssistedKg);
        return new CoachSession(workout.Id, exercise.ExerciseDefinitionId, sets.Max(s => s.CompletedAt), exercise.TrackingMode,
            last.WeightKg, last.AssistedKg, working.Length == 0 ? 0 : working.Min(s => s.Reps), working.Length,
            assessment?.Effort ?? 0, assessment?.Controlled, assessment?.Pain ?? false, unknown);
    }
}
