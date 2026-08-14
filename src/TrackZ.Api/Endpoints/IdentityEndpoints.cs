using MediatR;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TrackZ.Application.Identity.Login;
using TrackZ.Application.Identity.Logout;
using TrackZ.Application.Identity.Refresh;
using TrackZ.Application.Identity.Register;

namespace TrackZ.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth");

        auth.MapPost("/register", async (RegisterRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var fieldErrors = ValidateRegistrationRequest(request);
            if (fieldErrors is not null)
            {
                return Results.ValidationProblem(fieldErrors);
            }

            var registeredUser = await sender.Send(
                new RegisterCommand(request.Email!, request.Password!),
                cancellationToken);
            return Results.Created($"/api/v1/users/{registeredUser.UserId:D}", registeredUser);
        })
        .RequireRateLimiting("identity")
        .Accepts<RegisterRequest>("application/json")
        .Produces(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        auth.MapPost("/login", async (LoginRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var fieldErrors = ValidateLoginRequest(request);
            if (fieldErrors is not null)
            {
                return Results.ValidationProblem(fieldErrors);
            }

            var tokenPair = await sender.Send(
                new LoginCommand(request.Email!, request.Password!, request.DeviceName!),
                cancellationToken);
            return Results.Ok(tokenPair);
        })
        .RequireRateLimiting("identity")
        .Accepts<LoginRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        auth.MapPost("/refresh", async (RefreshRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken) || string.IsNullOrWhiteSpace(request.DeviceName))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["refreshToken"] = ["The refreshToken field is required."],
                    ["deviceName"] = ["The deviceName field is required."]
                });
            }

            return Results.Ok(await sender.Send(new RefreshCommand(request.RefreshToken, request.DeviceName), cancellationToken));
        })
        .RequireRateLimiting("identity")
        .Accepts<RefreshRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        auth.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal user, ISender sender, CancellationToken cancellationToken) =>
        {
            var subject = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(subject, out var userId))
            {
                return Results.Unauthorized();
            }

            await sender.Send(new LogoutCommand(userId, sessionId), cancellationToken);
            return Results.NoContent();
        })
        .RequireAuthorization();

        return endpoints;
    }

    private static Dictionary<string, string[]>? ValidateRegistrationRequest(RegisterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, "email", request.Email);
        AddRequired(errors, "password", request.Password);
        return errors.Count == 0 ? null : errors;
    }

    private static Dictionary<string, string[]>? ValidateLoginRequest(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, "email", request.Email);
        AddRequired(errors, "password", request.Password);
        AddRequired(errors, "deviceName", request.DeviceName);
        return errors.Count == 0 ? null : errors;
    }

    private static void AddRequired(IDictionary<string, string[]> errors, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = [$"The {name} field is required."];
        }
    }

    private sealed record RegisterRequest(string? Email, string? Password);

    private sealed record LoginRequest(string? Email, string? Password, string? DeviceName);

    private sealed record RefreshRequest(string? RefreshToken, string? DeviceName);
}
