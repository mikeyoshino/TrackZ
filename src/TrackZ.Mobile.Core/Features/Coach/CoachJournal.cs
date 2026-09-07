using System.Text.Json;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;

namespace TrackZ.Mobile.Features.Coach;

public sealed record CoachAssessment(Guid WorkoutId, Guid ExerciseId, DateTimeOffset At,
    int Effort, bool? Controlled, bool Pain, bool Accepted, Guid LastSetId);
public sealed record CoachRecovery(BodyPart BodyPart, DateOnly Week, DateTimeOffset At, bool Ready);
public sealed record CoachJournalData(
    List<CoachAssessment> Assessments, Dictionary<Guid, bool> Warmups, List<CoachRecovery> Recovery)
{
    public static CoachJournalData Empty() => new([], [], []);
}

/// <summary>Device-local coaching metadata. Cleared atomically with private workout data.</summary>
public sealed class CoachJournal(TrackZLocalDatabase database)
{
    public Task<CoachJournalData> ReadAsync(CancellationToken token = default) => database.ReadAsync(async (connection, ct) =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM CoachJournal WHERE Id = 1";
        var json = await command.ExecuteScalarAsync(ct) as string;
        return json is null ? CoachJournalData.Empty() : JsonSerializer.Deserialize<CoachJournalData>(json)
            ?? throw new InvalidDataException("Coaching journal is invalid.");
    }, token);

    public Task SaveAssessmentAsync(CoachAssessment assessment, CancellationToken token = default)
    {
        if (assessment.WorkoutId == Guid.Empty || assessment.ExerciseId == Guid.Empty || assessment.LastSetId == Guid.Empty
            || assessment.Effort is < 0 or > 3 || assessment.Accepted && (assessment.Pain || assessment.Controlled != true))
            throw new ArgumentException("Invalid exercise check-in.", nameof(assessment));
        return UpdateAsync(data =>
        {
            data.Assessments.RemoveAll(a => a.WorkoutId == assessment.WorkoutId && a.ExerciseId == assessment.ExerciseId);
            data.Assessments.Add(assessment);
        }, token);
    }

    public Task SetWarmupAsync(Guid setId, bool warmup, CancellationToken token = default)
    {
        if (setId == Guid.Empty) throw new ArgumentException("Set ID is required.", nameof(setId));
        return UpdateAsync(data => data.Warmups[setId] = warmup, token);
    }

    public Task SaveControlAsync(CoachSession session, bool controlled, DateTimeOffset now, CancellationToken token = default)
    {
        if (session.LastSetId == Guid.Empty || session.WorkoutId == Guid.Empty || session.ExerciseId == Guid.Empty)
            throw new ArgumentException("A recorded session is required.", nameof(session));
        return UpdateAsync(data =>
        {
            var previous = data.Assessments.LastOrDefault(a => a.WorkoutId == session.WorkoutId
                && a.ExerciseId == session.ExerciseId && a.LastSetId == session.LastSetId && a.At >= session.LastEditedAt);
            var assessment = new CoachAssessment(session.WorkoutId, session.ExerciseId, now,
                previous?.Effort ?? session.Effort, controlled, previous?.Pain ?? session.Pain, false, session.LastSetId);
            data.Assessments.RemoveAll(a => a.WorkoutId == session.WorkoutId && a.ExerciseId == session.ExerciseId);
            data.Assessments.Add(assessment);
        }, token);
    }

    public Task SaveRecoveryAsync(CoachRecovery recovery, CancellationToken token = default) => UpdateAsync(data =>
    {
        data.Recovery.RemoveAll(r => r.BodyPart == recovery.BodyPart && r.Week == recovery.Week);
        data.Recovery.Add(recovery);
    }, token);

    private Task UpdateAsync(Action<CoachJournalData> update, CancellationToken token) => database.WriteAsync(async (connection, transaction, ct) =>
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Payload FROM CoachJournal WHERE Id = 1";
        var json = await command.ExecuteScalarAsync(ct) as string;
        var data = json is null ? CoachJournalData.Empty() : JsonSerializer.Deserialize<CoachJournalData>(json)
            ?? throw new InvalidDataException("Coaching journal is invalid.");
        update(data);
        command.CommandText = "INSERT INTO CoachJournal (Id, Payload) VALUES (1, $payload) ON CONFLICT(Id) DO UPDATE SET Payload = excluded.Payload";
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(data));
        await command.ExecuteNonQueryAsync(ct);
        return true;
    }, token);
}
