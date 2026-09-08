using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Domain.Muscles;

namespace TrackZ.Application.Tests;

public sealed class MuscleCoverageQueryTests
{
    [Fact]
    public async Task Query_reads_only_current_users_week_and_propagates_cancellation()
    {
        var user = new CurrentUser();
        using var cancellation = new CancellationTokenSource();
        var store = new Store(user.UserId, cancellation.Token);
        var result = await new GetMuscleCoverageHandler(store, user).Handle(
            new(new DateOnly(2026, 9, 7)), cancellation.Token);
        Assert.Equal(new DateOnly(2026, 9, 7), result.Start);
        Assert.Equal("Asia/Bangkok", result.TimeZoneId);
    }
    private sealed class CurrentUser : ICurrentUser { public Guid UserId { get; } = Guid.NewGuid(); }
    private sealed class Store(Guid allowedUser, CancellationToken expectedToken) : IMuscleCoverageReadStore
    {
        public Task<MuscleCoverageReport> GetAsync(Guid userId, DateOnly? week, CancellationToken cancellationToken)
        {
            if (userId != allowedUser) throw new UnauthorizedAccessException();
            Assert.Equal(expectedToken, cancellationToken);
            Assert.Equal(new DateOnly(2026, 9, 7), week);
            return Task.FromResult(MuscleCoverageCalculator.Build([], week!.Value, week.Value.AddDays(6),
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"), DateTimeOffset.Parse("2026-09-08T12:00:00Z")));
        }
    }
}
