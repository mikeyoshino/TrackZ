using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using TrackZ.Api.Middleware;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;
using TrackZ.Domain.Exercises;

namespace TrackZ.Api.Endpoints;

public static class ExerciseEndpoints
{
    public static IEndpointRouteBuilder MapExerciseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/exercises", async (HttpRequest request, HttpContext context, ISender sender, IExerciseCursorCodec cursorCodec, CancellationToken cancellationToken) =>
        {
            var parsed = ParseRequest(request, context);
            if (parsed.Error is not null) return parsed.Error;
            if (parsed.Cursor is not null)
            {
                try { _ = cursorCodec.Decode(parsed.Cursor); }
                catch (BusinessException) { return ValidationProblem(context, new Dictionary<string, string[]> { ["cursor"] = [InvalidField(context, "cursor")] }); }
            }

            var page = await sender.Send(new ListExercisesQuery(
                parsed.BodyPart,
                parsed.Search,
                parsed.Cursor,
                parsed.PageSize), cancellationToken);
            return Results.Ok(page);
        })
        .RequireAuthorization()
        .Produces(StatusCodes.Status200OK)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        return endpoints;
    }

    private static (BodyPart? BodyPart, string? Search, string? Cursor, int PageSize, IResult? Error) ParseRequest(HttpRequest request, HttpContext context)
    {
        var errors = new Dictionary<string, string[]>();
        BodyPart? bodyPart = null;
        if (request.Query.TryGetValue("bodyPart", out var bodyPartValues))
        {
            if (bodyPartValues.Count != 1 || !Enum.TryParse<BodyPart>(bodyPartValues[0], true, out var parsedBodyPart) || !Enum.IsDefined(parsedBodyPart))
            {
                errors["bodyPart"] = [InvalidField(context, "bodyPart")];
            }
            else
            {
                bodyPart = parsedBodyPart;
            }
        }

        var pageSize = 30;
        if (request.Query.TryGetValue("pageSize", out var pageSizeValues))
        {
            if (pageSizeValues.Count != 1
                || !int.TryParse(pageSizeValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || pageSize < 1)
            {
                errors["pageSize"] = [InvalidField(context, "pageSize")];
            }
        }
        pageSize = Math.Min(pageSize, 50);

        var search = request.Query.TryGetValue("search", out var searchValues) && searchValues.Count == 1
            ? searchValues[0]?.Trim()
            : null;
        if (request.Query.TryGetValue("search", out searchValues) && (searchValues.Count != 1 || search?.Length > 100))
        {
            errors["search"] = [InvalidField(context, "search")];
        }

        var cursor = request.Query.TryGetValue("cursor", out var cursorValues) && cursorValues.Count == 1
            ? cursorValues[0]
            : null;
        if (request.Query.TryGetValue("cursor", out cursorValues) && (cursorValues.Count != 1 || string.IsNullOrWhiteSpace(cursor) || cursor.Length > 1024))
        {
            errors["cursor"] = [InvalidField(context, "cursor")];
        }

        return errors.Count == 0
            ? (bodyPart, string.IsNullOrWhiteSpace(search) ? null : search, cursor, pageSize, null)
            : (null, null, null, 0, ValidationProblem(context, errors));
    }

    private static IResult ValidationProblem(HttpContext context, IReadOnlyDictionary<string, string[]> errors) => Results.Json(
        new ApiProblemDetails("https://api.trackz.app/problems/validation", "Validation failed", StatusCodes.Status400BadRequest,
            BusinessErrorCode.InvalidRequest,
            BusinessMessages.Get(BusinessErrorCode.InvalidRequest, CultureInfo.CurrentUICulture, "Request data is invalid."),
            context.TraceIdentifier, errors.ToDictionary(pair => pair.Key, pair => pair.Value)),
        contentType: "application/problem+json", statusCode: StatusCodes.Status400BadRequest);

    private static string InvalidField(HttpContext context, string field) =>
        BusinessMessages.Format("InvalidField", CultureInfo.CurrentUICulture, field);
}

public sealed class HttpCurrentUser(IHttpContextAccessor contextAccessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var subject = contextAccessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? contextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(subject, out var userId) ? userId : Guid.Empty;
        }
    }
}
