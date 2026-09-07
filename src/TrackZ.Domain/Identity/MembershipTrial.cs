namespace TrackZ.Domain.Identity;

/// <summary>Registration-based access. This never creates a payment authorization.</summary>
public sealed record MembershipTrial(DateTimeOffset StartsAt, DateTimeOffset EndsAt)
{
    public static MembershipTrial ForRegistration(DateTimeOffset registeredAt)
    {
        var start = registeredAt.ToUniversalTime();
        return new(start, start.AddMonths(1));
    }

    public bool IsActive(DateTimeOffset now) => now >= StartsAt && now < EndsAt;
}
