using MediatR;
using TrackZ.Application.Identity.Login;
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
        .Accepts<LoginRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .ProducesValidationProblem();

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
}
