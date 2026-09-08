using TrackZ.Domain.Muscles;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Exercises;

namespace TrackZ.Mobile.Features.Progress;

public sealed record MuscleCoverageSnapshot(MuscleCoverageReport Report, IReadOnlyList<CachedExercise> Exercises,
    IReadOnlySet<Guid> ActiveExerciseIds);

public sealed class MuscleCoverageSource(ILocalWorkoutRepository workouts, ExerciseCache cache,
    CoachJournal journal, IClock clock, TimeZoneInfo zone)
{
    public async Task<MuscleCoverageSnapshot> LoadAsync(DateOnly week, CancellationToken token = default)
    {
        var history = await workouts.GetHistoryAsync(token);
        var active = await workouts.GetActiveAsync(token);
        var exercises = await cache.GetAllAsync(token);
        var data = await journal.ReadAsync(token);
        var all = (active is null ? history : new[] { active }.Concat(history)).DistinctBy(w => w.Id).ToArray();
        var start = week.AddDays(-(((int)week.DayOfWeek + 6) % 7));
        var facts = Facts(all, exercises, data, clock.UtcNow);
        return new(MuscleCoverageCalculator.Build(facts, start, start.AddDays(6), zone, clock.UtcNow), exercises,
            active?.Exercises.Where(e => e.DeletedAt is null).Select(e => e.ExerciseDefinitionId).ToHashSet() ?? []);
    }

    public static IReadOnlyList<MuscleSetFact> Facts(IReadOnlyList<LocalWorkout> workouts,
        IReadOnlyList<CachedExercise> definitions, CoachJournalData journal, DateTimeOffset now)
    {
        var defs = definitions.ToDictionary(d => d.Id);
        // Legacy journal is deliberately not substituted for synchronized set classification.
        // Otherwise the server and this device would display different coverage for the same sets.
        return workouts.Where(w => w.DeletedAt is null && w.StartedAt <= now
                && w.Status is LocalWorkoutStatus.Active or LocalWorkoutStatus.Completed)
            .SelectMany(w => w.Exercises.Where(e => e.DeletedAt is null)
                .SelectMany(e => e.Sets.Where(s => s.DeletedAt is null && s.CompletedAt <= now)
                    .Select(s => new MuscleSetFact(s.Id, e.ExerciseDefinitionId, s.CompletedAt, s.IsWarmup,
                        defs.TryGetValue(e.ExerciseDefinitionId, out var definition) ? definition.IsCustom
                            : MuscleCatalog.Find(e.ExerciseDefinitionId) is null))))
            .DistinctBy(s => s.SetId).ToArray();
    }
}
