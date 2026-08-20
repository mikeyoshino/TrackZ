namespace TrackZ.Contracts.Gamification;

public sealed record GamificationProfileDto(
    int TotalXp,
    int Level,
    int CurrentLevelRequiredXp,
    int? NextLevelRequiredXp,
    int WeeklyGoal,
    int WeeklyCompletedWorkouts,
    int CurrentStreakWeeks,
    int BestStreakWeeks,
    IReadOnlyList<EarnedBadgeDto> Badges,
    IReadOnlyList<string> NewlyEarnedBadgeKeys);

public sealed record EarnedBadgeDto(
    string Key,
    string NameResourceKey,
    string DescriptionResourceKey,
    string IconKey,
    DateTimeOffset EarnedAt);

public sealed record UpdateMotivationPreferencesRequest(int WeeklyGoal, string TimeZoneId);
