using MediatR;
using TrackZ.Application.Gamification.GetProfile;
using TrackZ.Contracts.Gamification;
using TrackZ.Application.Gamification.UpdatePreferences;

namespace TrackZ.Api.Endpoints;

public static class GamificationEndpoints
{
    public static IEndpointRouteBuilder MapGamificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/gamification/profile", async (
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetGamificationProfileQuery(), cancellationToken)))
            .RequireAuthorization()
            .Produces<GamificationProfileDto>(StatusCodes.Status200OK);
        endpoints.MapPut("/api/v1/gamification/preferences", async (
            UpdateMotivationPreferencesRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(
                new UpdateMotivationPreferencesCommand(request.WeeklyGoal, request.TimeZoneId),
                cancellationToken)))
            .RequireAuthorization()
            .Produces<GamificationProfileDto>(StatusCodes.Status200OK);
        return endpoints;
    }
}
