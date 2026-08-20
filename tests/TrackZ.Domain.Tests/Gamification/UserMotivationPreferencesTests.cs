using TrackZ.Domain.Identity;

namespace TrackZ.Domain.Tests.Gamification;

public sealed class UserMotivationPreferencesTests
{
    [Fact]
    public void User_has_safe_defaults_and_accepts_a_valid_goal_and_iana_time_zone()
    {
        var user = User.Create("athlete@example.com", "hash");

        Assert.Equal(3, user.WeeklyWorkoutGoal);
        Assert.Equal("UTC", user.TimeZoneId);

        user.UpdateMotivationPreferences(5, "Asia/Bangkok");

        Assert.Equal(5, user.WeeklyWorkoutGoal);
        Assert.Equal("Asia/Bangkok", user.TimeZoneId);
    }

    [Theory]
    [InlineData(0, "Asia/Bangkok")]
    [InlineData(8, "Asia/Bangkok")]
    [InlineData(3, "Not/A_TimeZone")]
    public void Invalid_goal_or_time_zone_is_rejected(int goal, string timeZoneId)
    {
        var user = User.Create("athlete@example.com", "hash");

        Assert.ThrowsAny<ArgumentException>(() =>
            user.UpdateMotivationPreferences(goal, timeZoneId));
    }
}
