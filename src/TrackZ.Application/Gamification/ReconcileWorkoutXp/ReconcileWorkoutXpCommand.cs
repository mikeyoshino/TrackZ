using System.Security.Cryptography;
using System.Text;
using MediatR;
using TrackZ.Domain.Gamification;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Application.Gamification.ReconcileWorkoutXp;

public sealed record ReconcileWorkoutXpCommand(Guid WorkoutId) : IRequest;

public sealed record WorkoutXpState(
    Guid UserId,
    Guid WorkoutId,
    long Version,
    bool IsEligible,
    int ValidSetCount);

public interface IGamificationStore
{
    Task<IAppDbTransaction> BeginGamificationTransactionAsync(CancellationToken cancellationToken);
    Task AcquireWorkoutXpLockAsync(Guid workoutId, CancellationToken cancellationToken);
    Task AcquireUserProgressLockAsync(Guid userId, CancellationToken cancellationToken);
    Task<WorkoutXpState?> GetWorkoutXpStateAsync(Guid workoutId, CancellationToken cancellationToken);
    Task<IReadOnlyList<XpLedgerEntry>> ListWorkoutXpEntriesAsync(
        Guid userId,
        Guid workoutId,
        CancellationToken cancellationToken);
    Task<int> GetUserTotalXpAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LevelThreshold>> ListLevelThresholdsAsync(CancellationToken cancellationToken);
    Task<UserProgress?> FindUserProgressAsync(Guid userId, CancellationToken cancellationToken);
    void AddXpLedgerEntry(XpLedgerEntry entry);
    void AddUserProgress(UserProgress progress);
    Task SaveGamificationAsync(CancellationToken cancellationToken);
}

public sealed class ReconcileWorkoutXpHandler(
    IGamificationStore store,
    TimeProvider timeProvider) : IRequestHandler<ReconcileWorkoutXpCommand>
{
    public async Task Handle(ReconcileWorkoutXpCommand request, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(request.WorkoutId, Guid.Empty);
        await using var transaction = await store.BeginGamificationTransactionAsync(cancellationToken);
        await store.AcquireWorkoutXpLockAsync(request.WorkoutId, cancellationToken);
        var state = await store.GetWorkoutXpStateAsync(request.WorkoutId, cancellationToken);
        if (state is null) return;
        await store.AcquireUserProgressLockAsync(state.UserId, cancellationToken);

        var entries = await store.ListWorkoutXpEntriesAsync(
            state.UserId,
            state.WorkoutId,
            cancellationToken);
        var desiredXp = state.IsEligible
            ? XpRules.ForCompletedWorkout(state.ValidSetCount)
            : 0;
        var existingXp = entries.Sum(entry => entry.Amount);
        var now = timeProvider.GetUtcNow();
        var additions = new List<XpLedgerEntry>();

        if (entries.Count == 0 && desiredXp > 0)
        {
            additions.Add(XpLedgerEntry.Create(
                state.UserId,
                XpLedgerReason.WorkoutCompleted,
                state.WorkoutId,
                state.WorkoutId,
                XpRules.CompletedWorkoutXp,
                now));
            var setXp = desiredXp - XpRules.CompletedWorkoutXp;
            if (setXp > 0)
            {
                additions.Add(XpLedgerEntry.Create(
                    state.UserId,
                    XpLedgerReason.WorkoutSets,
                    DeriveSourceId(state.WorkoutId, "sets"),
                    state.WorkoutId,
                    setXp,
                    now));
            }
        }
        else if (desiredXp != existingXp)
        {
            additions.Add(XpLedgerEntry.Create(
                state.UserId,
                XpLedgerReason.Correction,
                DeriveSourceId(state.WorkoutId, $"correction:{state.Version}:{desiredXp}"),
                state.WorkoutId,
                desiredXp - existingXp,
                now));
        }

        if (additions.Count == 0) return;

        var currentTotalXp = await store.GetUserTotalXpAsync(state.UserId, cancellationToken);
        var thresholds = await store.ListLevelThresholdsAsync(cancellationToken);
        var rulesVersion = thresholds.Select(threshold => threshold.RulesVersion).DefaultIfEmpty(1).Max();
        var activeThresholds = thresholds.Where(threshold => threshold.RulesVersion == rulesVersion);
        var newTotalXp = currentTotalXp + additions.Sum(entry => entry.Amount);
        var level = LevelThreshold.ResolveLevel(newTotalXp, activeThresholds);
        var progress = await store.FindUserProgressAsync(state.UserId, cancellationToken);
        foreach (var entry in additions) store.AddXpLedgerEntry(entry);
        if (progress is null)
        {
            store.AddUserProgress(UserProgress.Create(
                state.UserId, newTotalXp, level, rulesVersion, now));
        }
        else
        {
            progress.Recalculate(newTotalXp, level, rulesVersion, now);
        }

        await store.SaveGamificationAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Guid DeriveSourceId(Guid originId, string discriminator)
    {
        var input = Encoding.UTF8.GetBytes($"{originId:D}:{discriminator}");
        return new Guid(SHA256.HashData(input).AsSpan(0, 16));
    }
}
