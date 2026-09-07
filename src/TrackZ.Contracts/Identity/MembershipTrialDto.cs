namespace TrackZ.Contracts.Identity;

public sealed record MembershipTrialDto(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsActive,
    bool AutoRenews);
