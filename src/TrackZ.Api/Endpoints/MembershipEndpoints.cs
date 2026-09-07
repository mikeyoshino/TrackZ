using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrackZ.Contracts.Identity;
using TrackZ.Domain.Identity;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Api.Endpoints;

public static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/membership/trial", async (
            ClaimsPrincipal principal, AppDbContext db, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(subject, out var userId)) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null) return Results.Unauthorized();
            var trial = MembershipTrial.ForRegistration(user.CreatedAt);
            return Results.Ok(new MembershipTrialDto(trial.StartsAt, trial.EndsAt, trial.IsActive(clock.GetUtcNow()), false));
        }).RequireAuthorization().Produces<MembershipTrialDto>();
        return endpoints;
    }
}
