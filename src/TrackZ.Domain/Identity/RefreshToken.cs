namespace TrackZ.Domain.Identity;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public Guid SessionId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public User User { get; private set; } = null!;

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        Guid sessionId,
        DateTimeOffset expiresAt,
        DateTimeOffset? createdAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);

        var normalizedCreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var normalizedExpiresAt = expiresAt.ToUniversalTime();

        if (normalizedExpiresAt <= normalizedCreatedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "The refresh token must expire after it is created.");
        }

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            SessionId = sessionId,
            ExpiresAt = normalizedExpiresAt,
            CreatedAt = normalizedCreatedAt
        };
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAt = revokedAt.ToUniversalTime();
    }
}
