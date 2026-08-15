using MediatR;
using TrackZ.Application.Sync.Push;
using TrackZ.Contracts.Sync;

namespace TrackZ.Api.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/sync/push", async (
            SyncPushRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new PushSyncCommand(request.Operations), cancellationToken)))
        .RequireAuthorization()
        .Produces<SyncPushResponse>(StatusCodes.Status200OK);

        return endpoints;
    }
}
