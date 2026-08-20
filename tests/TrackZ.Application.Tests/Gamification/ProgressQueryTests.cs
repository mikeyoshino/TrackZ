using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Gamification.GetProfile;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;

namespace TrackZ.Application.Tests.Gamification;

public sealed class ProgressQueryTests
{
    [Fact]
    public async Task Queries_return_only_the_authenticated_users_projection()
    {
        var userId = Guid.NewGuid();
        var progress = new ProgressSummaryDto(12500m, 3200m, 12, 4, []);
        var profile = new GamificationProfileDto(
            2750, 4, 1500, 2500, 4, 3, 2, 5, [], []);
        var store = new InMemoryProgressReadStore(userId, progress, profile);
        var currentUser = new CurrentUser(userId);

        var summary = await new GetProgressSummaryHandler(store, currentUser)
            .Handle(new GetProgressSummaryQuery(), CancellationToken.None);
        var gamification = await new GetGamificationProfileHandler(store, currentUser)
            .Handle(new GetGamificationProfileQuery(), CancellationToken.None);

        Assert.Same(progress, summary);
        Assert.Same(profile, gamification);
    }

    private sealed record CurrentUser(Guid UserId) : ICurrentUser;

    private sealed class InMemoryProgressReadStore(
        Guid expectedUserId,
        ProgressSummaryDto progress,
        GamificationProfileDto profile) : IProgressReadStore
    {
        public Task<ProgressSummaryDto> GetProgressSummaryAsync(Guid userId, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedUserId, userId);
            return Task.FromResult(progress);
        }

        public Task<GamificationProfileDto> GetGamificationProfileAsync(Guid userId, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedUserId, userId);
            return Task.FromResult(profile);
        }
    }
}
