using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Gamification.GetProfile;
using TrackZ.Application.Gamification.UpdatePreferences;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;
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

    [Fact]
    public async Task Motivation_preferences_validate_and_update_the_authenticated_user()
    {
        var userId = Guid.NewGuid();
        var profile = new GamificationProfileDto(0, 1, 0, 250, 5, 0, 0, 0, [], []);
        var projections = new InMemoryProgressReadStore(
            userId,
            new ProgressSummaryDto(0, 0, 0, 0, []),
            profile);
        var preferences = new RecordingPreferenceStore();
        var handler = new UpdateMotivationPreferencesHandler(
            preferences, projections, new CurrentUser(userId));

        var result = await handler.Handle(
            new UpdateMotivationPreferencesCommand(5, "Asia/Bangkok"),
            CancellationToken.None);

        Assert.Same(profile, result);
        Assert.Equal((userId, 5, "Asia/Bangkok"), Assert.Single(preferences.Updates));
        var invalid = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new UpdateMotivationPreferencesCommand(8, "Asia/Bangkok"),
            CancellationToken.None));
        Assert.Equal(BusinessErrorCode.InvalidRequest, invalid.Code);
        Assert.Equal(400, invalid.StatusCode);
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

    private sealed class RecordingPreferenceStore : IMotivationPreferenceStore
    {
        public List<(Guid UserId, int Goal, string TimeZoneId)> Updates { get; } = [];
        public Task UpdateAsync(Guid userId, int weeklyGoal, string timeZoneId, CancellationToken cancellationToken)
        {
            Updates.Add((userId, weeklyGoal, timeZoneId));
            return Task.CompletedTask;
        }
    }
}
