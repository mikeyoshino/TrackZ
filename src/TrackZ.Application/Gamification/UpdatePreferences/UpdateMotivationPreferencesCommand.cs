using MediatR;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Contracts.Gamification;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;

namespace TrackZ.Application.Gamification.UpdatePreferences;

public sealed record UpdateMotivationPreferencesCommand(int WeeklyGoal, string TimeZoneId)
    : IRequest<GamificationProfileDto>;

public interface IMotivationPreferenceStore
{
    Task UpdateAsync(Guid userId, int weeklyGoal, string timeZoneId, CancellationToken cancellationToken);
}

public sealed class UpdateMotivationPreferencesHandler(
    IMotivationPreferenceStore preferences,
    IProgressReadStore progress,
    ICurrentUser currentUser) : IRequestHandler<UpdateMotivationPreferencesCommand, GamificationProfileDto>
{
    public async Task<GamificationProfileDto> Handle(
        UpdateMotivationPreferencesCommand request,
        CancellationToken cancellationToken)
    {
        if (request.WeeklyGoal is < 1 or > 7 || string.IsNullOrWhiteSpace(request.TimeZoneId))
            throw InvalidRequest();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw InvalidRequest();
        }
        catch (InvalidTimeZoneException)
        {
            throw InvalidRequest();
        }
        await preferences.UpdateAsync(
            currentUser.UserId, request.WeeklyGoal, request.TimeZoneId, cancellationToken);
        return await progress.GetGamificationProfileAsync(currentUser.UserId, cancellationToken);
    }

    private static BusinessException InvalidRequest() =>
        new(BusinessErrorCode.InvalidRequest, "Motivation preferences are invalid.", 400);
}
