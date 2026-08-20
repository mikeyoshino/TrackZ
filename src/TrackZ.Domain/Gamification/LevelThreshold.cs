namespace TrackZ.Domain.Gamification;

public sealed class LevelThreshold
{
    private LevelThreshold()
    {
    }

    public Guid Id { get; private set; }
    public int Level { get; private set; }
    public int RequiredXp { get; private set; }
    public int RulesVersion { get; private set; }

    public static LevelThreshold Create(int level, int requiredXp, int rulesVersion)
        => Create(Guid.NewGuid(), level, requiredXp, rulesVersion);

    public static LevelThreshold Create(Guid id, int level, int requiredXp, int rulesVersion)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredXp);
        ArgumentOutOfRangeException.ThrowIfLessThan(rulesVersion, 1);
        return new LevelThreshold
        {
            Id = id,
            Level = level,
            RequiredXp = requiredXp,
            RulesVersion = rulesVersion
        };
    }

    public static int ResolveLevel(int totalXp, IEnumerable<LevelThreshold> thresholds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalXp);
        ArgumentNullException.ThrowIfNull(thresholds);
        return thresholds
            .Where(threshold => threshold.RequiredXp <= totalXp)
            .OrderByDescending(threshold => threshold.RequiredXp)
            .ThenByDescending(threshold => threshold.Level)
            .Select(threshold => threshold.Level)
            .FirstOrDefault(1);
    }
}
