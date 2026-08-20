using MediatR;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Gamification;

namespace TrackZ.Application.Gamification.EvaluateBadges;

public sealed record EvaluateBadgesCommand(Guid UserId) : IRequest<BadgeEvaluationResult>;

public sealed record BadgeEvaluationResult(IReadOnlyList<string> NewlyEarnedBadgeKeys);

public sealed record BadgeFacts(
    int CompletedWorkoutCount,
    int BestStreakWeeks,
    int DistinctExerciseCount,
    int PersonalRecordCount);

public interface IBadgeStore
{
    Task<IAppDbTransaction> BeginBadgeTransactionAsync(CancellationToken cancellationToken);
    Task AcquireBadgeLockAsync(Guid userId, CancellationToken cancellationToken);
    Task<BadgeFacts?> GetBadgeFactsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BadgeDefinition>> ListBadgeDefinitionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<UserBadge>> ListUserBadgesAsync(Guid userId, CancellationToken cancellationToken);
    void AddUserBadge(UserBadge badge);
    void RemoveUserBadge(UserBadge badge);
    void AddBadgeAuditEvent(BadgeAuditEvent auditEvent);
    Task SaveBadgesAsync(CancellationToken cancellationToken);
}

public sealed class EvaluateBadgesHandler(IBadgeStore store, TimeProvider timeProvider)
    : IRequestHandler<EvaluateBadgesCommand, BadgeEvaluationResult>
{
    public async Task<BadgeEvaluationResult> Handle(
        EvaluateBadgesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(request.UserId, Guid.Empty);
        await using var transaction = await store.BeginBadgeTransactionAsync(cancellationToken);
        await store.AcquireBadgeLockAsync(request.UserId, cancellationToken);
        var facts = await store.GetBadgeFactsAsync(request.UserId, cancellationToken);
        if (facts is null) return new BadgeEvaluationResult([]);

        var definitions = await store.ListBadgeDefinitionsAsync(cancellationToken);
        var current = await store.ListUserBadgesAsync(request.UserId, cancellationToken);
        var currentByDefinitionId = current.ToDictionary(badge => badge.BadgeDefinitionId);
        var newlyEarned = new List<string>();
        var changed = false;
        var now = timeProvider.GetUtcNow();
        foreach (var definition in definitions.OrderBy(definition => definition.Key, StringComparer.Ordinal))
        {
            var shouldBeEarned = definition.IsEarned(
                facts.CompletedWorkoutCount,
                facts.BestStreakWeeks,
                facts.DistinctExerciseCount,
                facts.PersonalRecordCount);
            var isEarned = currentByDefinitionId.TryGetValue(definition.Id, out var badge);
            if (shouldBeEarned && !isEarned)
            {
                store.AddUserBadge(UserBadge.Create(request.UserId, definition, now));
                store.AddBadgeAuditEvent(BadgeAuditEvent.Create(
                    request.UserId, definition, BadgeAuditAction.Awarded, now));
                newlyEarned.Add(definition.Key);
                changed = true;
            }
            else if (!shouldBeEarned && isEarned)
            {
                store.RemoveUserBadge(badge!);
                store.AddBadgeAuditEvent(BadgeAuditEvent.Create(
                    request.UserId, definition, BadgeAuditAction.Revoked, now));
                changed = true;
            }
        }

        if (changed)
        {
            await store.SaveBadgesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new BadgeEvaluationResult(newlyEarned);
    }
}
