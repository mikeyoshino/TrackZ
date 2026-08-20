using TrackZ.Application.Gamification.EvaluateBadges;
using TrackZ.Domain.Gamification;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Application.Tests.Gamification;

public sealed class BadgeEvaluationTests
{
    [Fact]
    public async Task Fourth_goal_week_awards_streak_badge_once_and_reconciles_after_history_removal()
    {
        var userId = Guid.NewGuid();
        var store = new InMemoryBadgeStore(userId, new BadgeFacts(0, 4, 0, 0));
        var handler = new EvaluateBadgesHandler(store, TimeProvider.System);

        var first = await handler.Handle(new EvaluateBadgesCommand(userId), CancellationToken.None);
        var second = await handler.Handle(new EvaluateBadgesCommand(userId), CancellationToken.None);

        Assert.Equal(["streak-4"], first.NewlyEarnedBadgeKeys);
        Assert.Empty(second.NewlyEarnedBadgeKeys);
        Assert.Equal("streak-4", Assert.Single(store.CurrentBadges).BadgeKey);

        store.Facts = store.Facts with { BestStreakWeeks = 0 };
        await handler.Handle(new EvaluateBadgesCommand(userId), CancellationToken.None);

        Assert.Empty(store.CurrentBadges);
        Assert.Equal(
            [BadgeAuditAction.Awarded, BadgeAuditAction.Revoked],
            store.AuditEvents.Select(item => item.Action));
    }

    private sealed class InMemoryBadgeStore(Guid userId, BadgeFacts facts) : IBadgeStore
    {
        public BadgeFacts Facts { get; set; } = facts;
        public List<UserBadge> CurrentBadges { get; } = [];
        public List<BadgeAuditEvent> AuditEvents { get; } = [];

        public Task<IAppDbTransaction> BeginBadgeTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAppDbTransaction>(new Transaction());

        public Task AcquireBadgeLockAsync(Guid requestedUserId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<BadgeFacts?> GetBadgeFactsAsync(Guid requestedUserId, CancellationToken cancellationToken) =>
            Task.FromResult<BadgeFacts?>(requestedUserId == userId ? Facts : null);

        public Task<IReadOnlyList<BadgeDefinition>> ListBadgeDefinitionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BadgeDefinition>>([
                BadgeDefinition.Create(
                    Guid.Parse("50000000-0000-0000-0000-000000000005"),
                    "streak-4",
                    BadgeCriteria.BestStreakWeeks,
                    4,
                    "Badge_Streak4_Name",
                    "Badge_Streak4_Description",
                    "badge-streak-4",
                    criteriaVersion: 1)
            ]);

        public Task<IReadOnlyList<UserBadge>> ListUserBadgesAsync(Guid requestedUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserBadge>>(CurrentBadges.ToArray());

        public void AddUserBadge(UserBadge badge) => CurrentBadges.Add(badge);
        public void RemoveUserBadge(UserBadge badge) => CurrentBadges.Remove(badge);
        public void AddBadgeAuditEvent(BadgeAuditEvent auditEvent) => AuditEvents.Add(auditEvent);
        public Task SaveBadgesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class Transaction : IAppDbTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
