namespace TrackZ.Domain.Gamification;

public enum XpLedgerReason
{
    WorkoutCompleted = 1,
    WorkoutSets = 2,
    WeeklyGoal = 3,
    Correction = 4
}

public sealed class XpLedgerEntry
{
    private XpLedgerEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public XpLedgerReason Reason { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid OriginId { get; private set; }
    public int Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static XpLedgerEntry Create(
        Guid userId,
        XpLedgerReason reason,
        Guid sourceId,
        Guid originId,
        int amount,
        DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(originId, Guid.Empty);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (amount == 0 || (reason != XpLedgerReason.Correction && amount < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (createdAt == default) throw new ArgumentOutOfRangeException(nameof(createdAt));
        return new XpLedgerEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Reason = reason,
            SourceId = sourceId,
            OriginId = originId,
            Amount = amount,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }
}
