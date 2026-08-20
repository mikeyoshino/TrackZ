namespace TrackZ.Domain.Gamification;

public sealed class UserBadge
{
    private UserBadge()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BadgeDefinitionId { get; private set; }
    public string BadgeKey { get; private set; } = null!;
    public DateTimeOffset EarnedAt { get; private set; }

    public static UserBadge Create(Guid userId, BadgeDefinition definition, DateTimeOffset earnedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(definition);
        if (earnedAt == default) throw new ArgumentOutOfRangeException(nameof(earnedAt));
        return new UserBadge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BadgeDefinitionId = definition.Id,
            BadgeKey = definition.Key,
            EarnedAt = earnedAt.ToUniversalTime()
        };
    }
}

public enum BadgeAuditAction
{
    Awarded = 1,
    Revoked = 2
}

public sealed class BadgeAuditEvent
{
    private BadgeAuditEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BadgeDefinitionId { get; private set; }
    public string BadgeKey { get; private set; } = null!;
    public BadgeAuditAction Action { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static BadgeAuditEvent Create(
        Guid userId,
        BadgeDefinition definition,
        BadgeAuditAction action,
        DateTimeOffset occurredAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(definition);
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (occurredAt == default) throw new ArgumentOutOfRangeException(nameof(occurredAt));
        return new BadgeAuditEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BadgeDefinitionId = definition.Id,
            BadgeKey = definition.Key,
            Action = action,
            OccurredAt = occurredAt.ToUniversalTime()
        };
    }
}
