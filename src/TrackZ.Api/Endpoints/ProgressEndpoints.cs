using MediatR;
using TrackZ.Application.Progress.GetSummary;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Muscles;

namespace TrackZ.Api.Endpoints;

public static class ProgressEndpoints
{
    public static IEndpointRouteBuilder MapProgressEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/progress/muscles", async (
            DateOnly? week, ISender sender, CancellationToken cancellationToken) =>
        {
            // Bound arithmetic and pathological historic queries, without exposing user internals.
            if (week is { Year: < 2000 or > 2100 })
                return Results.BadRequest(new { message = "เลือกวันที่ระหว่างปี 2000–2100" });
            return Results.Ok(await sender.Send(new GetMuscleCoverageQuery(week), cancellationToken));
        }).RequireAuthorization().Produces<MuscleCoverageReport>();
        endpoints.MapGet("/api/v1/progress/summary", async (
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetProgressSummaryQuery(), cancellationToken)))
            .RequireAuthorization()
            .Produces<ProgressSummaryDto>(StatusCodes.Status200OK);
        return endpoints;
    }
}
