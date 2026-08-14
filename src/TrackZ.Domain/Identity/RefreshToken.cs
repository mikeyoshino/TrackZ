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

    public string DeviceName { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public User User { get; private set; } = null!;

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        Guid sessionId,
        DateTimeOffset expiresAt,
        DateTimeOffset? createdAt = null,
        string deviceName = "unknown")
    {
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        var normalizedDeviceName = NormalizeDeviceName(deviceName);

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
            DeviceName = normalizedDeviceName,
            ExpiresAt = normalizedExpiresAt,
            CreatedAt = normalizedCreatedAt
        };
    }

    public static string NormalizeDeviceName(string deviceName)
    {
        var normalized = deviceName?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length is 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceName), "A device name between 1 and 128 characters is required.");
        }

        return normalized;
    }

    public void Revoke(DateTimeOffset revokedAt)
    {
        RevokedAt = revokedAt.ToUniversalTime();
    }
}
