using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using TrackZ.Api.Middleware;
using TrackZ.Application.Exercises.CreateCustom;
using TrackZ.Application.Exercises.DeleteCustom;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Exercises.UpdateCustom;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
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

        var custom = endpoints.MapGroup("/api/v1/exercises/custom").RequireAuthorization();
        custom.MapPost("", async (HttpRequest request, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            var parsed = await ReadCustomRequestAsync<CreateCustomExerciseRequest>(request, context, cancellationToken);
            if (parsed.Error is not null) return parsed.Error;
            var model = parsed.Value!;
            var errors = ValidateCustomRequest(model.Name, model.BodyPart, model.TrackingMode, model.LibraryImageId, model.UploadedImageKey, context, trackingModeRequired: true);
            if (errors is not null) return ValidationProblem(context, errors);

            var id = await sender.Send(new CreateCustomExerciseCommand(
                model.Name!, model.BodyPart!.Value, model.TrackingMode!.Value, model.LibraryImageId, model.UploadedImageKey), cancellationToken);
            return Results.Created($"/api/v1/exercises/custom/{id:D}", new { id });
        })
        .Produces(StatusCodes.Status201Created)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        custom.MapPut("/{id:guid}", async (Guid id, HttpRequest request, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            var parsed = await ReadCustomRequestAsync<UpdateCustomExerciseRequest>(request, context, cancellationToken);
            if (parsed.Error is not null) return parsed.Error;
            var model = parsed.Value!;
            var errors = ValidateCustomRequest(model.Name, model.BodyPart, model.TrackingMode, model.LibraryImageId, model.UploadedImageKey, context, trackingModeRequired: false);
            if (errors is not null) return ValidationProblem(context, errors);

            await sender.Send(new UpdateCustomExerciseCommand(
                id, model.Name!, model.BodyPart!.Value, model.TrackingMode, model.LibraryImageId, model.UploadedImageKey), cancellationToken);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
        .Produces<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

        custom.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            await sender.Send(new DeleteCustomExerciseCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

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

    private static Dictionary<string, string[]>? ValidateCustomRequest(
        string? name,
        BodyPart? bodyPart,
        TrackingMode? trackingMode,
        Guid? libraryImageId,
        string? uploadedImageKey,
        HttpContext context,
        bool trackingModeRequired)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) errors["name"] = [InvalidField(context, "name")];
        if (bodyPart is not { } parsedBodyPart || !Enum.IsDefined(parsedBodyPart)) errors["bodyPart"] = [InvalidField(context, "bodyPart")];
        if (trackingModeRequired && (trackingMode is not { } parsedTrackingMode || !Enum.IsDefined(parsedTrackingMode))) errors["trackingMode"] = [InvalidField(context, "trackingMode")];
        if (!trackingModeRequired && trackingMode is { } updateTrackingMode && !Enum.IsDefined(updateTrackingMode)) errors["trackingMode"] = [InvalidField(context, "trackingMode")];
        if (libraryImageId is not null || uploadedImageKey is not null)
        {
            if (libraryImageId is not null) errors["libraryImageId"] = [InvalidField(context, "libraryImageId")];
            if (uploadedImageKey is not null) errors["uploadedImageKey"] = [InvalidField(context, "uploadedImageKey")];
        }

        return errors.Count == 0 ? null : errors;
    }

    private static async Task<(T? Value, IResult? Error)> ReadCustomRequestAsync<T>(HttpRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!HasSupportedJsonContentType(request.ContentType))
        {
            return (default, ValidationProblem(context, new Dictionary<string, string[]> { ["body"] = [InvalidField(context, "body")] }));
        }

        try
        {
            var value = await request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            return value is null
                ? (default, ValidationProblem(context, new Dictionary<string, string[]> { ["body"] = [InvalidField(context, "body")] }))
                : (value, null);
        }
        catch (JsonException exception)
        {
            var field = MapCustomJsonPath(exception.Path);
            return (default, ValidationProblem(context, new Dictionary<string, string[]> { [field] = [InvalidField(context, field)] }));
        }
    }

    private static bool HasSupportedJsonContentType(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType)) return false;
        var type = mediaType.MediaType.Value ?? string.Empty;
        if (!type.StartsWith("application/", StringComparison.OrdinalIgnoreCase)) return false;
        var subtype = type["application/".Length..];
        if (!string.Equals(subtype, "json", StringComparison.OrdinalIgnoreCase)
            && (!subtype.EndsWith("+json", StringComparison.OrdinalIgnoreCase) || subtype.Length == "+json".Length)) return false;
        var charsets = mediaType.Parameters.Where(parameter => string.Equals(parameter.Name.Value, "charset", StringComparison.OrdinalIgnoreCase)).ToList();
        return charsets.Count switch
        {
            0 => true,
            1 => string.Equals(charsets[0].Value.Value?.Trim('\"').Trim(), "utf-8", StringComparison.OrdinalIgnoreCase)
                || string.Equals(charsets[0].Value.Value?.Trim('\"').Trim(), "utf8", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string MapCustomJsonPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("$.", StringComparison.Ordinal)) return "body";
        var field = path[2..];
        return field is "name" or "bodyPart" or "trackingMode" or "libraryImageId" or "uploadedImageKey" ? field : "body";
    }
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
