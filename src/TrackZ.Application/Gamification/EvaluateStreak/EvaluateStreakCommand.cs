using MediatR;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Gamification;

namespace TrackZ.Application.Gamification.EvaluateStreak;

public sealed record EvaluateStreakCommand(Guid UserId) : IRequest;

public sealed record StreakEvaluationInput(
    Guid UserId,
    int WeeklyGoal,
    string TimeZoneId,
    IReadOnlyCollection<CompletedWorkoutInstant> CompletedWorkouts);

public interface IStreakStore
{
    Task<IAppDbTransaction> BeginStreakTransactionAsync(CancellationToken cancellationToken);
    Task AcquireUserProgressLockAsync(Guid userId, CancellationToken cancellationToken);
    Task<StreakEvaluationInput?> GetStreakEvaluationInputAsync(Guid userId, CancellationToken cancellationToken);
    Task<StreakState?> FindStreakStateAsync(Guid userId, CancellationToken cancellationToken);
    void AddStreakState(StreakState state);
    Task<IReadOnlyList<XpLedgerEntry>> ListWeeklyGoalXpEntriesAsync(Guid userId, CancellationToken cancellationToken);
    Task<int> GetUserTotalXpAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LevelThreshold>> ListLevelThresholdsAsync(CancellationToken cancellationToken);
    Task<UserProgress?> FindUserProgressAsync(Guid userId, CancellationToken cancellationToken);
    void AddXpLedgerEntry(XpLedgerEntry entry);
    void AddUserProgress(UserProgress progress);
    Task SaveStreakAsync(CancellationToken cancellationToken);
}

public sealed class EvaluateStreakHandler(IStreakStore store, TimeProvider timeProvider)
    : IRequestHandler<EvaluateStreakCommand>
{
    public async Task Handle(EvaluateStreakCommand request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(request.UserId, Guid.Empty);
        await using var transaction = await store.BeginStreakTransactionAsync(cancellationToken);
        await store.AcquireUserProgressLockAsync(request.UserId, cancellationToken);
        var input = await store.GetStreakEvaluationInputAsync(request.UserId, cancellationToken);
        if (input is null) return;

        var now = timeProvider.GetUtcNow();
        var rebuilt = WeeklyGoalCalculator.RebuildStreak(
            input.WeeklyGoal,
            input.TimeZoneId,
            input.CompletedWorkouts,
            now);
        var state = await store.FindStreakStateAsync(request.UserId, cancellationToken);
        if (state is null)
        {
            state = StreakState.Create(request.UserId);
            store.AddStreakState(state);
        }

        state.Recalculate(
            rebuilt.CurrentWeeks,
            rebuilt.BestWeeks,
            rebuilt.LastEvaluatedWeek,
            now);

        var existing = await store.ListWeeklyGoalXpEntriesAsync(request.UserId, cancellationToken);
        var groups = existing.GroupBy(entry => entry.OriginId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var desiredOrigins = rebuilt.GoalMetWeeks
            .ToDictionary(week => XpSourceId.ForWeek(request.UserId, week), week => week);
        var allOrigins = groups.Keys.Concat(desiredOrigins.Keys).Distinct().Order().ToArray();
        var additions = new List<XpLedgerEntry>();
        foreach (var originId in allOrigins)
        {
            var currentXp = groups.GetValueOrDefault(originId)?.Sum(entry => entry.Amount) ?? 0;
            var desiredXp = desiredOrigins.ContainsKey(originId) ? XpRules.WeeklyGoalXp : 0;
            if (currentXp == desiredXp) continue;

            var hasOriginalAward = groups.GetValueOrDefault(originId)?
                .Any(entry => entry.Reason == XpLedgerReason.WeeklyGoal) == true;
            additions.Add(XpLedgerEntry.Create(
                request.UserId,
                hasOriginalAward ? XpLedgerReason.Correction : XpLedgerReason.WeeklyGoal,
                hasOriginalAward ? Guid.NewGuid() : originId,
                originId,
                desiredXp - currentXp,
                now));
        }

        if (additions.Count > 0)
        {
            var currentTotalXp = await store.GetUserTotalXpAsync(request.UserId, cancellationToken);
            var thresholds = await store.ListLevelThresholdsAsync(cancellationToken);
            var rulesVersion = thresholds.Select(threshold => threshold.RulesVersion).DefaultIfEmpty(1).Max();
            var activeThresholds = thresholds.Where(threshold => threshold.RulesVersion == rulesVersion);
            var newTotalXp = currentTotalXp + additions.Sum(entry => entry.Amount);
            var level = LevelThreshold.ResolveLevel(newTotalXp, activeThresholds);
            var progress = await store.FindUserProgressAsync(request.UserId, cancellationToken);
            foreach (var entry in additions) store.AddXpLedgerEntry(entry);
            if (progress is null)
            {
                store.AddUserProgress(UserProgress.Create(
                    request.UserId, newTotalXp, level, rulesVersion, now));
            }
            else
            {
                progress.Recalculate(newTotalXp, level, rulesVersion, now);
            }
        }

        await store.SaveStreakAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

internal static class XpSourceId
{
    public static Guid ForWeek(Guid userId, YearWeek week) =>
        Derive(userId, $"iso-week:{week.Year:D4}-{week.Week:D2}");

    private static Guid Derive(Guid originId, string discriminator)
    {
        var input = System.Text.Encoding.UTF8.GetBytes($"{originId:D}:{discriminator}");
        return new Guid(System.Security.Cryptography.SHA256.HashData(input).AsSpan(0, 16));
    }
}
