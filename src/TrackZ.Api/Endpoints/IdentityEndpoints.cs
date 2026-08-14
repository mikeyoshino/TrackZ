using MediatR;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TrackZ.Application.Identity.Login;
using TrackZ.Application.Identity.Logout;
using TrackZ.Application.Identity.Refresh;
using TrackZ.Application.Identity.Register;
using TrackZ.Api.Middleware;
using TrackZ.Contracts.Errors;

namespace TrackZ.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth");

        auth.MapPost("/register", async (HttpRequest httpRequest, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            var parsed = await ReadIdentityRequestAsync<RegisterRequest>(httpRequest, context, cancellationToken);
            if (parsed.Error is not null) return parsed.Error;
            var request = parsed.Value!;
            var fieldErrors = ValidateRegistrationRequest(request, context);
            if (fieldErrors is not null)
            {
                return ValidationProblem(context, fieldErrors);
            }

            var registeredUser = await sender.Send(
                new RegisterCommand(request.Email!, request.Password!),
                cancellationToken);
            return Results.Created($"/api/v1/users/{registeredUser.UserId:D}", registeredUser);
        })
        .RequireRateLimiting("identity")
        .Produces(StatusCodes.Status201Created)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        auth.MapPost("/login", async (HttpRequest httpRequest, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            var parsed = await ReadIdentityRequestAsync<LoginRequest>(httpRequest, context, cancellationToken);
            if (parsed.Error is not null) return parsed.Error;
            var request = parsed.Value!;
            var fieldErrors = ValidateLoginRequest(request, context);
            if (fieldErrors is not null)
            {
                return ValidationProblem(context, fieldErrors);
            }

            var tokenPair = await sender.Send(
                new LoginCommand(request.Email!, request.Password!, request.DeviceName!),
                cancellationToken);
            return Results.Ok(tokenPair);
        })
        .RequireRateLimiting("identity")
        .Produces(StatusCodes.Status200OK)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        auth.MapPost("/refresh", async (HttpRequest httpRequest, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            var parsed = await ReadIdentityRequestAsync<RefreshRequest>(httpRequest, context, cancellationToken);
            if (parsed.Error is not null) return parsed.Error;
            var request = parsed.Value!;
            var fieldErrors = ValidateRefreshRequest(request, context);
            if (fieldErrors is not null)
            {
                return ValidationProblem(context, fieldErrors);
            }

            return Results.Ok(await sender.Send(new RefreshCommand(request.RefreshToken!, request.DeviceName!), cancellationToken));
        })
        .RequireRateLimiting("identity")
        .Produces(StatusCodes.Status200OK)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

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

        auth.MapPost("/logout", async (HttpRequest httpRequest, ClaimsPrincipal user, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            var parsed = await ReadIdentityRequestAsync<LogoutRequest>(httpRequest, context, cancellationToken);
            if (parsed.Error is not null) return parsed.Error;
            var request = parsed.Value!;
            if (request.SessionId is not { } sessionId || sessionId == Guid.Empty)
            {
                return ValidationProblem(context, new Dictionary<string, string[]> { ["sessionId"] = [RequiredMessage(context, "sessionId")] });
            }

            var subject = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(subject, out var userId)) return Results.Unauthorized();
            await sender.Send(new LogoutCommand(userId, sessionId), cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization().Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        return endpoints;
    }

    private static Dictionary<string, string[]>? ValidateRegistrationRequest(RegisterRequest request, HttpContext context)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, "email", request.Email, context);
        AddRequired(errors, "password", request.Password, context);
        return errors.Count == 0 ? null : errors;
    }

    private static Dictionary<string, string[]>? ValidateLoginRequest(LoginRequest request, HttpContext context)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, "email", request.Email, context);
        AddRequired(errors, "password", request.Password, context);
        AddRequired(errors, "deviceName", request.DeviceName, context);
        AddDeviceLength(errors, request.DeviceName, context);
        return errors.Count == 0 ? null : errors;
    }

    private static Dictionary<string, string[]>? ValidateRefreshRequest(RefreshRequest request, HttpContext context)
    {
        var errors = new Dictionary<string, string[]>();
        AddRequired(errors, "refreshToken", request.RefreshToken, context);
        AddRequired(errors, "deviceName", request.DeviceName, context);
        AddDeviceLength(errors, request.DeviceName, context);
        return errors.Count == 0 ? null : errors;
    }

    private static void AddRequired(IDictionary<string, string[]> errors, string name, string? value, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = [RequiredMessage(context, name)];
        }
    }

    private static void AddDeviceLength(IDictionary<string, string[]> errors, string? value, HttpContext context)
    {
        if (value?.Trim().Length > 128) errors["deviceName"] = [BusinessMessages.Format("InvalidDeviceName", CultureInfo.CurrentUICulture)];
    }

    private static IResult ValidationProblem(HttpContext context, IReadOnlyDictionary<string, string[]> errors) => Results.Json(
        new ApiProblemDetails("https://api.trackz.app/problems/validation", "Validation failed", StatusCodes.Status400BadRequest,
            BusinessErrorCode.InvalidRequest,
            BusinessMessages.Get(BusinessErrorCode.InvalidRequest, CultureInfo.CurrentUICulture, "Request data is invalid."),
            context.TraceIdentifier, errors.ToDictionary(pair => pair.Key, pair => pair.Value)),
        contentType: "application/problem+json", statusCode: StatusCodes.Status400BadRequest);

    private static string RequiredMessage(HttpContext context, string field) => BusinessMessages.Format("RequiredField", CultureInfo.CurrentUICulture, field);

    private static async Task<(T? Value, IResult? Error)> ReadIdentityRequestAsync<T>(HttpRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!HasSupportedJsonContentType(request.ContentType))
        {
            return (default, ValidationProblem(context, new Dictionary<string, string[]> { ["body"] = [InvalidMessage(context, "body")] }));
        }

        try
        {
            var value = await request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            return value is null
                ? (default, ValidationProblem(context, new Dictionary<string, string[]> { ["body"] = [InvalidMessage(context, "body")] }))
                : (value, null);
        }
        catch (JsonException exception)
        {
            var field = MapJsonPath(exception.Path);
            return (default, ValidationProblem(context, new Dictionary<string, string[]> { [field] = [InvalidMessage(context, field)] }));
        }
    }

    private static bool HasSupportedJsonContentType(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType)) return false;
        var type = mediaType.MediaType.Value ?? string.Empty;
        if (!string.Equals(type, "application/json", StringComparison.OrdinalIgnoreCase) && !type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) return false;
        var charset = mediaType.Charset.Value?.Trim('"');
        return charset is null or "" || string.Equals(charset, "utf-8", StringComparison.OrdinalIgnoreCase) || string.Equals(charset, "utf8", StringComparison.OrdinalIgnoreCase);
    }

    private static string MapJsonPath(string? path)
    {
        var field = path?.TrimStart('$', '.');
        return field is "email" or "password" or "deviceName" or "refreshToken" or "sessionId" ? field : "body";
    }

    private static string InvalidMessage(HttpContext context, string field) => BusinessMessages.Format("InvalidField", CultureInfo.CurrentUICulture, field);

    private sealed record RegisterRequest(string? Email, string? Password);

    private sealed record LoginRequest(string? Email, string? Password, string? DeviceName);

    private sealed record RefreshRequest(string? RefreshToken, string? DeviceName);

    private sealed record LogoutRequest(Guid? SessionId);
}
