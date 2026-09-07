using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Mobile.Features.Coach;

public enum CoachAction { Hold, AddRep, AddWeight, ChooseIncrement, CheckConsistency, CheckRecovery, Pain }

public sealed record CoachSession(
    Guid WorkoutId, Guid ExerciseId, DateTimeOffset At, TrackingMode Mode,
    decimal? WeightKg, decimal? AssistedKg, int Reps, int WorkingSets,
    int Effort, bool? Controlled, bool Pain, bool HasUnknownSets)
{
    public bool IsCompleted { get; init; } = true;
    public int UnclassifiedSets { get; init; }
    public Guid LastSetId { get; init; }
    public DateTimeOffset LastEditedAt { get; init; }
}

public sealed record CoachRecommendation(CoachAction Action, int? Reps = null, decimal? WeightKg = null)
{
    public bool IsIncrease => Action is CoachAction.AddRep or CoachAction.AddWeight;
}

/// <summary>
/// Conservative product rules, not a diagnosis or a measurement of muscle growth.
/// The 8–12 rep progression range and relative volume threshold are UX heuristics.
/// </summary>
public static class TrainingCoachPolicy
{
    public static CoachRecommendation Evaluate(IEnumerable<CoachSession> history, DateTimeOffset now, decimal? incrementKg = null)
    {
        var sessions = history.Where(s => s.At <= now).DistinctBy(s => s.WorkoutId)
            .OrderByDescending(s => s.At).ToArray();
        if (sessions.Length == 0) return new(CoachAction.Hold);
        var latest = sessions[0];
        if (now - latest.At > TimeSpan.FromDays(14)) return new(CoachAction.Hold);
        if (latest.Pain) return new(CoachAction.Pain);
        if (latest.Controlled == false || latest.Effort == 3) return new(CoachAction.Hold);
        if (sessions.Length < 2 || latest.HasUnknownSets || latest.WorkingSets < 1)
            return new(CoachAction.Hold);
        var previous = sessions[1];
        var comparable = previous.ExerciseId == latest.ExerciseId && previous.Mode == latest.Mode
            && previous.WeightKg == latest.WeightKg && previous.AssistedKg == latest.AssistedKg
            && previous.WorkingSets == latest.WorkingSets && !previous.HasUnknownSets
            && latest.At - previous.At <= TimeSpan.FromDays(14)
            && latest.At - previous.At >= TimeSpan.FromHours(18);
        if (!comparable) return new(CoachAction.Hold);
        if (latest.Controlled != true || previous.Controlled != true || previous.Pain
            || latest.Effort != 1 || previous.Effort != 1)
        {
            // A check-in, never an increase merely because time has elapsed.
            var stable = sessions.Take(3).ToArray();
            return new(stable.Length == 3 && stable.All(s => s.Reps == latest.Reps && s.WeightKg == latest.WeightKg
                && s.Mode == latest.Mode && !s.Pain && !s.HasUnknownSets)
                && latest.At - stable[^1].At >= TimeSpan.FromDays(14)
                ? CoachAction.CheckConsistency : CoachAction.Hold);
        }
        if (latest.Reps < 8 || previous.Reps < 8) return new(CoachAction.Hold);
        if (latest.Reps < 12 && latest.Reps >= previous.Reps)
            return new(CoachAction.AddRep, latest.Reps + 1, latest.WeightKg);
        if (latest.Reps < 12 || previous.Reps < 12) return new(CoachAction.Hold);
        if (latest.Mode != TrackingMode.Weighted || latest.WeightKg is not > 0m)
            return new(CoachAction.Hold);
        if (incrementKg is not > 0m) return new(CoachAction.ChooseIncrement);
        var weight = latest.WeightKg.Value + incrementKg.Value;
        return weight <= SetMeasurement.MaximumKilograms
            ? new(CoachAction.AddWeight, 8, weight) : new(CoachAction.Hold);
    }

    public static bool NeedsRecoveryCheck(int currentWeekSets, IReadOnlyList<int> precedingWeeks)
    {
        // Require three complete weeks with actual training; never project a partial week.
        if (precedingWeeks.Count != 3 || precedingWeeks.Any(n => n < 1)) return false;
        var baseline = precedingWeeks.Average();
        return currentWeekSets >= baseline * 1.3 && currentWeekSets - baseline >= 4;
    }
}
