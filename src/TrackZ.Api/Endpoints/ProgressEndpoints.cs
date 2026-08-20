using MediatR;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Contracts.Progress;

namespace TrackZ.Api.Endpoints;

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/progress/summary", async (
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetProgressSummaryQuery(), cancellationToken)))
            .RequireAuthorization()
            .Produces<ProgressSummaryDto>(StatusCodes.Status200OK);
        return endpoints;
    }
}
