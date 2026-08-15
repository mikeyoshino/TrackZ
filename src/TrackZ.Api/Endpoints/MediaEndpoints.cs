using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Net.Http.Headers;
using TrackZ.Api.Middleware;
using TrackZ.Application.Media.CompleteUpload;
using TrackZ.Application.Media.RequestUpload;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;

namespace TrackZ.Api.Endpoints;
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/media/exercise-images/uploads").RequireAuthorization();
        group.MapPost("", async (HttpRequest request, HttpContext context, ISender sender, CancellationToken cancellationToken) =>
        {
            if (!IsJson(request.ContentType)) return Bad(context, "body");
            try
            {
                var body = await request.ReadFromJsonAsync<RequestImageUploadRequest>(cancellationToken: cancellationToken);
                if (body?.ExerciseId is not { } exerciseId || string.IsNullOrWhiteSpace(body.ContentType) || body.Length is not { } length) return Bad(context, "body");
                return Results.Ok(await sender.Send(new RequestImageUploadCommand(exerciseId, body.ContentType, length), cancellationToken));
            }
            catch (JsonException exception) { return Bad(context, MapPath(exception.Path)); }
        });
        group.MapPost("/{uploadId:guid}/complete", async (Guid uploadId, ISender sender, CancellationToken cancellationToken) => Results.Ok(await sender.Send(new CompleteImageUploadCommand(uploadId), cancellationToken)));
        return endpoints;
    }
    private static bool IsJson(string? type) => MediaTypeHeaderValue.TryParse(type, out var media) && string.Equals(media.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);
    private static string MapPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("$.", StringComparison.Ordinal)) return "body";
        var property = path[2..];
        if (property.IndexOfAny(['.', '[', ']', '$']) >= 0) return "body";
        return property.ToLowerInvariant() switch { "exerciseid" => "exerciseId", "contenttype" => "contentType", "length" => "length", _ => "body" };
    }
    private static IResult Bad(HttpContext context, string field) => Results.Json(new ApiProblemDetails("https://api.trackz.app/problems/validation", "Validation failed", 400, BusinessErrorCode.InvalidRequest, BusinessMessages.Get(BusinessErrorCode.InvalidRequest, CultureInfo.CurrentUICulture, "Request data is invalid."), context.TraceIdentifier, new Dictionary<string, string[]> { [field] = [BusinessMessages.Format("InvalidField", CultureInfo.CurrentUICulture, field)] }), contentType: "application/problem+json", statusCode: 400);
}
