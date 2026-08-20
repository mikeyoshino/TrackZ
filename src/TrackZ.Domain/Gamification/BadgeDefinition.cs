namespace TrackZ.Domain.Gamification;

public enum BadgeCriteria
{
    CompletedWorkouts = 1,
    BestStreakWeeks = 2,
    DistinctExercises = 3,
    PersonalRecords = 4
}

public sealed class BadgeDefinition
{
    private BadgeDefinition()
    {
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public BadgeCriteria Criteria { get; private set; }
    public int Threshold { get; private set; }
    public string NameResourceKey { get; private set; } = null!;
    public string DescriptionResourceKey { get; private set; } = null!;
    public string IconKey { get; private set; } = null!;
    public int CriteriaVersion { get; private set; }

    public static BadgeDefinition Create(
        Guid id,
        string key,
        BadgeCriteria criteria,
        int threshold,
        string nameResourceKey,
        string descriptionResourceKey,
        string iconKey,
        int criteriaVersion)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!Enum.IsDefined(criteria)) throw new ArgumentOutOfRangeException(nameof(criteria));
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameResourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionResourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(criteriaVersion, 1);
        return new BadgeDefinition
        {
            Id = id,
            Key = key.Trim(),
            Criteria = criteria,
            Threshold = threshold,
            NameResourceKey = nameResourceKey.Trim(),
            DescriptionResourceKey = descriptionResourceKey.Trim(),
            IconKey = iconKey.Trim(),
            CriteriaVersion = criteriaVersion
        };
    }

    public bool IsEarned(int completedWorkouts, int bestStreakWeeks, int distinctExercises, int personalRecords) =>
        Criteria switch
        {
            BadgeCriteria.CompletedWorkouts => completedWorkouts >= Threshold,
            BadgeCriteria.BestStreakWeeks => bestStreakWeeks >= Threshold,
            BadgeCriteria.DistinctExercises => distinctExercises >= Threshold,
            BadgeCriteria.PersonalRecords => personalRecords >= Threshold,
            _ => false
        };
}
