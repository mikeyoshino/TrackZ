using MediatR;
using TrackZ.Application.Sync.Push;
using TrackZ.Application.Sync.Pull;
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

        endpoints.MapGet("/api/v1/sync/pull", async (
            string? cursor,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(
                new PullSyncQuery(cursor, pageSize ?? 100), cancellationToken)))
        .RequireAuthorization()
        .Produces<SyncPullResponse>(StatusCodes.Status200OK);

        return endpoints;
    }
}
