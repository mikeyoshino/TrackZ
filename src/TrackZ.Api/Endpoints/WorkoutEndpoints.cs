using System.Globalization;
using MediatR;
using TrackZ.Api.Middleware;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Exercises.GetHistory;
using TrackZ.Application.Workouts;
using TrackZ.Application.Workouts.GetWorkout;
using TrackZ.Application.Workouts.ListHistory;
using TrackZ.Contracts.Errors;

namespace TrackZ.Api.Endpoints;

public static class WorkoutEndpoints
{
    public static IEndpointRouteBuilder MapWorkoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/workouts", async (
            HttpRequest request,
            HttpContext context,
            ISender sender,
            IWorkoutCursorCodec cursorCodec,
            CancellationToken cancellationToken) =>
        {
            var parsed = ParsePage(request, context, cursorCodec);
            if (parsed.Error is not null) return parsed.Error;
            var result = await sender.Send(
                new ListWorkoutHistoryQuery(parsed.Cursor, parsed.PageSize),
                cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .Produces(StatusCodes.Status200OK)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        endpoints.MapGet("/api/v1/workouts/{id:guid}", async (
            Guid id,
            ISender sender,
            CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetWorkoutQuery(id), cancellationToken)))
        .RequireAuthorization()
        .Produces(StatusCodes.Status200OK)
        .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

        endpoints.MapGet("/api/v1/exercises/{id:guid}/history", async (
            Guid id,
            HttpRequest request,
            HttpContext context,
            ISender sender,
            IWorkoutCursorCodec cursorCodec,
            CancellationToken cancellationToken) =>
        {
            var parsed = ParsePage(request, context, cursorCodec);
            if (parsed.Error is not null) return parsed.Error;
            var result = await sender.Send(
                new GetExerciseHistoryQuery(id, parsed.Cursor, parsed.PageSize),
                cancellationToken);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .Produces(StatusCodes.Status200OK)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

        return endpoints;
    }

    private static (string? Cursor, int PageSize, IResult? Error) ParsePage(
        HttpRequest request,
        HttpContext context,
        IWorkoutCursorCodec cursorCodec)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var pageSize = 20;
        if (request.Query.TryGetValue("pageSize", out var pageSizeValues)
            && (pageSizeValues.Count != 1
                || !int.TryParse(pageSizeValues[0], NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                || pageSize is < 1 or > 50))
        {
            errors["pageSize"] = [InvalidField("pageSize")];
        }

        string? cursor = null;
        if (request.Query.TryGetValue("cursor", out var cursorValues))
        {
            cursor = cursorValues.Count == 1 ? cursorValues[0] : null;
            if (cursorValues.Count != 1 || string.IsNullOrWhiteSpace(cursor) || cursor.Length > 1024)
            {
                errors["cursor"] = [InvalidField("cursor")];
            }
            else
            {
                try
                {
                    _ = cursorCodec.Decode(cursor);
                }
                catch (BusinessException)
                {
                    errors["cursor"] = [InvalidField("cursor")];
                }
            }
        }

        return errors.Count == 0
            ? (cursor, pageSize, null)
            : (null, 0, ValidationProblem(context, errors));
    }

    private static string InvalidField(string field) =>
        BusinessMessages.Format("InvalidField", CultureInfo.CurrentUICulture, field);

    private static IResult ValidationProblem(
        HttpContext context,
        IReadOnlyDictionary<string, string[]> errors) => Results.Json(
        new ApiProblemDetails(
            "https://api.trackz.app/problems/validation",
            "Validation failed",
            StatusCodes.Status400BadRequest,
            BusinessErrorCode.InvalidRequest,
            BusinessMessages.Get(
                BusinessErrorCode.InvalidRequest,
                CultureInfo.CurrentUICulture,
                "Request data is invalid."),
            context.TraceIdentifier,
            errors.ToDictionary(pair => pair.Key, pair => pair.Value)),
        contentType: "application/problem+json",
        statusCode: StatusCodes.Status400BadRequest);
}
