namespace TrackZ.Domain.Identity;

public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = null!;

    public string NormalizedEmail { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public ICollection<RefreshToken> RefreshTokens { get; } = new List<RefreshToken>();

    public static User Create(string email, string passwordHash, DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var trimmedEmail = email.Trim();

        return new User
        {
            Id = Guid.NewGuid(),
            Email = trimmedEmail,
            NormalizedEmail = trimmedEmail.ToUpperInvariant(),
            PasswordHash = passwordHash,
            CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
    }
}
