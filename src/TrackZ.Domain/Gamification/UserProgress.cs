namespace TrackZ.Domain.Gamification;

public sealed class UserProgress
{
    private UserProgress()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public int TotalXp { get; private set; }
    public int Level { get; private set; }
    public int RulesVersion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UserProgress Create(
        Guid userId,
        int totalXp,
        int level,
        int rulesVersion,
        DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        var progress = new UserProgress { Id = Guid.NewGuid(), UserId = userId };
        progress.Recalculate(totalXp, level, rulesVersion, updatedAt);
        return progress;
    }

    public void Recalculate(int totalXp, int level, int rulesVersion, DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalXp);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rulesVersion, 1);
        if (updatedAt == default) throw new ArgumentOutOfRangeException(nameof(updatedAt));
        TotalXp = totalXp;
        Level = level;
        RulesVersion = rulesVersion;
        UpdatedAt = updatedAt.ToUniversalTime();
    }
}
