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

    public int WeeklyWorkoutGoal { get; private set; }

    public string TimeZoneId { get; private set; } = null!;

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
            CreatedAt = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            WeeklyWorkoutGoal = 3,
            TimeZoneId = "UTC"
        };
    }

    public void UpdateMotivationPreferences(int weeklyWorkoutGoal, string timeZoneId)
    {
        if (weeklyWorkoutGoal is < 1 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(weeklyWorkoutGoal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var normalizedTimeZoneId = timeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("The time-zone identifier is not recognized.", nameof(timeZoneId), exception);
        }

        WeeklyWorkoutGoal = weeklyWorkoutGoal;
        TimeZoneId = normalizedTimeZoneId;
    }
}
