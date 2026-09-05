using System.Text.Json;
using TrackZ.Mobile.Data;

namespace TrackZ.Mobile.Features.Profile;

public sealed record TrainingScheduleRevision(DateOnly EffectiveFrom, DayOfWeek[] Days, DateOnly GoalFrom);
public sealed record TrainingGoalRevision(DateOnly Week, int Goal);

/// <summary>Effective-dated planning metadata; completed workouts are never inputs to an edit.</summary>
public sealed record TrainingSchedule(TrainingScheduleRevision[] Revisions, int? InitialGoal, TrainingGoalRevision[]? Goals = null)
{
    public static TrainingSchedule Empty => new([], null);
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public TrainingScheduleRevision? At(DateOnly date) => Revisions
        .Where(revision => revision.EffectiveFrom <= date).OrderByDescending(revision => revision.EffectiveFrom).FirstOrDefault();
    public bool IsPlanned(DateOnly date) => At(date)?.Days.Contains(date.DayOfWeek) == true;
    public int GoalForWeek(DateOnly date, int fallback) => Goals?.Where(goal => goal.Week <= Monday(date))
        .OrderByDescending(goal => goal.Week).FirstOrDefault()?.Goal ?? Revisions.Where(revision => revision.GoalFrom <= Monday(date))
        .OrderByDescending(revision => revision.EffectiveFrom).FirstOrDefault()?.Days.Length ?? InitialGoal ?? fallback;

    public TrainingSchedule Change(DateOnly today, IEnumerable<DayOfWeek> days, bool nextWeek, int currentGoal)
    {
        var selected = days.Distinct().OrderBy(day => ((int)day + 6) % 7).ToArray();
        if (selected.Length == 0 || selected.Any(day => !Enum.IsDefined(day)))
            throw new ArgumentException("Select at least one valid weekday.", nameof(days));
        var effective = nextWeek ? Monday(today).AddDays(7) : today;
        var week = Monday(today);
        // Day edits can replace today's plan, but cannot replace an already effective weekly target.
        var goals = (Goals ?? Revisions.Select(revision => new TrainingGoalRevision(revision.GoalFrom, revision.Days.Length)).ToArray())
            .Where(goal => goal.Week < week)
            .Append(new TrainingGoalRevision(week, GoalForWeek(today, Math.Clamp(currentGoal, 1, 7))))
            .Append(new TrainingGoalRevision(week.AddDays(7), selected.Length)).ToArray();
        return new TrainingSchedule(Revisions.Where(revision => revision.EffectiveFrom < effective)
            .Append(new TrainingScheduleRevision(effective, selected, Monday(today).AddDays(7)))
            .OrderBy(revision => revision.EffectiveFrom).ToArray(),
            InitialGoal ?? Math.Clamp(currentGoal, 1, 7), goals);
    }
}

/// <summary>Device-local plan history, removed with private account data.</summary>
public sealed class TrainingScheduleStore(TrackZLocalDatabase database)
{
    public Task<TrainingSchedule> ReadAsync(CancellationToken token = default) => database.ReadAsync(async (connection, ct) =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM TrainingSchedule WHERE Id = 1";
        return Parse(await command.ExecuteScalarAsync(ct) as string);
    }, token);

    public Task<TrainingSchedule> SaveAsync(DateOnly today, IEnumerable<DayOfWeek> days, bool nextWeek,
        int currentGoal, CancellationToken token = default) => database.WriteAsync(async (connection, transaction, ct) =>
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Payload FROM TrainingSchedule WHERE Id = 1";
        var schedule = Parse(await command.ExecuteScalarAsync(ct) as string).Change(today, days, nextWeek, currentGoal);
        command.CommandText = "INSERT INTO TrainingSchedule (Id, Payload) VALUES (1, $payload) ON CONFLICT(Id) DO UPDATE SET Payload = excluded.Payload";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(schedule));
        await command.ExecuteNonQueryAsync(ct);
        return schedule;
    }, token);

    private static TrainingSchedule Parse(string? json) => json is null ? TrainingSchedule.Empty
        : JsonSerializer.Deserialize<TrainingSchedule>(json) ?? throw new InvalidDataException("Invalid training schedule.");
}
