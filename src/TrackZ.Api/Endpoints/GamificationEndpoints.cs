using MediatR;
using TrackZ.Application.Gamification.GetProfile;
using TrackZ.Contracts.Gamification;

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
        return endpoints;
    }
}
