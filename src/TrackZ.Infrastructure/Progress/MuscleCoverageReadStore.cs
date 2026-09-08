using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Domain.Muscles;
using TrackZ.Domain.Workouts;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Infrastructure.Progress;

public sealed class MuscleCoverageReadStore(AppDbContext database, TimeProvider clock) : IMuscleCoverageReadStore
{
    public async Task<MuscleCoverageReport> GetAsync(Guid userId, DateOnly? week, CancellationToken cancellationToken)
    {
        var zoneId = await database.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.TimeZoneId).SingleAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var now = clock.GetUtcNow();
        var day = week ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var start = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var end = start.AddDays(6);
        // Broad UTC envelope safely contains the seven local dates, including DST transitions.
        var earliest = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var latest = new DateTimeOffset(end.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero).AddDays(1);
        var facts = await (
            from workout in database.WorkoutSessions.AsNoTracking()
            join exercise in database.WorkoutExercises.AsNoTracking() on workout.Id equals exercise.WorkoutSessionId
            join set in database.SetEntries.AsNoTracking() on exercise.Id equals set.WorkoutExerciseId
            join definition in database.Exercises.AsNoTracking() on exercise.ExerciseDefinitionId equals definition.Id
            where workout.OwnerId == userId && workout.DeletedAt == null
                && (workout.Status == WorkoutStatus.Active || workout.Status == WorkoutStatus.Completed)
                && workout.StartedAt <= now && exercise.DeletedAt == null && set.DeletedAt == null
                && set.CompletedAt >= earliest && set.CompletedAt <= latest && set.CompletedAt <= now
                && (definition.OwnerId == null || definition.OwnerId == userId)
            select new MuscleSetFact(set.Id, definition.Id, set.CompletedAt, set.IsWarmup, definition.OwnerId != null))
            .ToListAsync(cancellationToken);
        return MuscleCoverageCalculator.Build(facts, start, end, zone, now);
    }
}
