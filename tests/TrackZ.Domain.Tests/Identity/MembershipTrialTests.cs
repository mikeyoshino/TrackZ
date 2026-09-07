using TrackZ.Domain.Identity;

namespace TrackZ.Domain.Tests.Identity;

public sealed class MembershipTrialTests
{
    [Theory]
    [InlineData("2026-01-31T10:00:00Z", "2026-02-28T10:00:00Z")]
    [InlineData("2028-01-31T10:00:00Z", "2028-02-29T10:00:00Z")]
    [InlineData("2026-09-06T10:00:00Z", "2026-10-06T10:00:00Z")]
    public void Trial_lasts_one_calendar_month(string start, string end)
    {
        var trial = MembershipTrial.ForRegistration(DateTimeOffset.Parse(start));
        Assert.Equal(DateTimeOffset.Parse(end), trial.EndsAt);
        Assert.True(trial.IsActive(trial.StartsAt));
        Assert.True(trial.IsActive(trial.EndsAt.AddTicks(-1)));
        Assert.False(trial.IsActive(trial.EndsAt));
        Assert.False(trial.IsActive(trial.StartsAt.AddTicks(-1)));
    }

    [Fact]
    public void Reading_trial_later_does_not_restart_it()
    {
        var createdAt = DateTimeOffset.Parse("2026-09-06T10:00:00+07:00");
        var trial = MembershipTrial.ForRegistration(createdAt);
        Assert.Equal(TimeSpan.Zero, trial.StartsAt.Offset);
        Assert.False(trial.IsActive(createdAt.AddMonths(2)));
        Assert.Equal(trial, MembershipTrial.ForRegistration(createdAt));
    }
}
