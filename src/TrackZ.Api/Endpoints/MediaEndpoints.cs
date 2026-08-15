using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Net.Http.Headers;
using TrackZ.Api.Middleware;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Application.Media;
using TrackZ.Application.Media.CompleteUpload;
using TrackZ.Application.Media.RequestUpload;
using TrackZ.Contracts.Errors;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;

namespace TrackZ.Api.Endpoints;
public static class MediaEndpoints
{
    private const long MaxBytes = 5_000_000;
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/media/exercise-images/uploads").RequireAuthorization();
        group.MapPost("", RequestAsync);
        group.MapPut("/{uploadId:guid}/content", UploadContentAsync);
        group.MapPost("/{uploadId:guid}/complete", async (Guid uploadId, ISender sender, CancellationToken cancellationToken) => Results.Ok(await sender.Send(new CompleteImageUploadCommand(uploadId), cancellationToken)));
        var images = endpoints.MapGroup("/api/v1/media/exercise-images").RequireAuthorization();
        images.MapGet("/{imageId:guid}/{rendition}", ReadAsync);
        return endpoints;
    }
    private static async Task<IResult> RequestAsync(HttpRequest request, HttpContext context, ISender sender, CancellationToken cancellationToken)
    {
        if (!IsJson(request.ContentType)) return Bad(context, "body");
        try
        {
            var body = await request.ReadFromJsonAsync<RequestImageUploadRequest>(cancellationToken: cancellationToken);
            if (body is null) return Bad(context, "body");
            if (body.ExerciseId is not { } id || id == Guid.Empty) return Bad(context, "exerciseId");
            if (string.IsNullOrWhiteSpace(body.ContentType)) return Bad(context, "contentType");
            if (body.Length is not { } length) return Bad(context, "length");
            return Results.Ok(await sender.Send(new RequestImageUploadCommand(id, body.ContentType, length), cancellationToken));
        }
        catch (JsonException exception) { return Bad(context, MapPath(exception.Path)); }
    }
    private static async Task<IResult> UploadContentAsync(Guid uploadId, HttpRequest request, HttpContext context, IExerciseImageUploadStore store, IObjectStorage storage, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var ticket = await store.FindOwnedTicketAsync(uploadId, currentUser.UserId, cancellationToken);
        if (ticket is null || ticket.IsExpired(DateTimeOffset.UtcNow)) return Missing(context);
        if (!string.Equals(request.ContentType, ticket.DeclaredContentType, StringComparison.Ordinal)) return Bad(context, "contentType");
        if (request.ContentLength != ticket.DeclaredLength) return Bad(context, "length");
        UploadClaim claim;
        try { claim = await store.TryClaimUploadAsync(uploadId, currentUser.UserId, TimeSpan.FromMinutes(2), cancellationToken); }
        catch (InvalidOperationException) { return Missing(context); }
        var bytes = await BufferAsync(request.Body, cancellationToken);
        if (bytes.LongLength != ticket.DeclaredLength) return Bad(context, "length");
        await using var content = new MemoryStream(bytes, writable: false);
        await storage.PutAsync($"staging/{currentUser.UserId:D}/", claim.StagingObjectKey, content, ticket.DeclaredContentType, cancellationToken);
        StagingUploadTransition transition;
        try
        {
            transition = await store.TryMarkUploadedAsync(ticket.Id, currentUser.UserId, claim.UploadLeaseId, cancellationToken);
        }
        catch
        {
            await DeleteStagingBestEffortAsync(storage, currentUser.UserId, claim.StagingObjectKey);
            throw;
        }
        if (transition == StagingUploadTransition.Uploaded) return Results.NoContent();
        // Every admitted request has its own lease-scoped key. A late loser can therefore
        // delete its own bytes without risking the winner's staged object.
        await DeleteStagingBestEffortAsync(storage, currentUser.UserId, claim.StagingObjectKey);
        return Missing(context);
    }
    private static async Task<IResult> ReadAsync(Guid imageId, string rendition, HttpContext context, IExerciseImageUploadStore store, IObjectStorage storage, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (rendition is not ("master" or "thumbnail")) return Missing(context);
        var image = await store.FindReadableImageAsync(imageId, currentUser.UserId, cancellationToken);
        if (image is null) return Missing(context);
        var key = rendition == "master" ? image.MasterObjectKey : image.ThumbnailObjectKey;
        var prefix = image.IsPrivate ? $"private/{currentUser.UserId:D}/" : "system/";
        var result = await storage.GetAsync(prefix, key, cancellationToken);
        return result is null ? Missing(context) : Results.Stream(result.Content, result.ContentType);
    }
    private static async Task<byte[]> BufferAsync(Stream source, CancellationToken cancellationToken)
    { await using var buffer = new MemoryStream((int)MaxBytes + 1); var chunk = new byte[81920]; while (true) { var count = await source.ReadAsync(chunk, cancellationToken); if (count == 0) break; if (buffer.Length + count > MaxBytes) throw new TrackZ.Application.Common.Exceptions.BusinessException(BusinessErrorCode.ImageTooLarge, "The image is too large.", 400); await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken); } return buffer.ToArray(); }
    private static async Task DeleteStagingBestEffortAsync(IObjectStorage storage, Guid ownerId, string stagingKey)
    { try { await storage.DeleteAsync($"staging/{ownerId:D}/", stagingKey, CancellationToken.None); } catch { } }
    private static IResult Missing(HttpContext context) => Results.Json(new ApiProblemDetails(
        "https://api.trackz.app/problems/business-rule-violation",
        "Business rule violation",
        StatusCodes.Status404NotFound,
        BusinessErrorCode.ExerciseNotFound,
        BusinessMessages.Get(BusinessErrorCode.ExerciseNotFound, CultureInfo.CurrentUICulture, "The exercise was not found."),
        context.TraceIdentifier,
        null), contentType: "application/problem+json", statusCode: StatusCodes.Status404NotFound);
    private static bool IsJson(string? contentType)
    { if (!MediaTypeHeaderValue.TryParse(contentType, out var media)) return false; var type = media.MediaType.Value ?? string.Empty; if (!type.Equals("application/json", StringComparison.OrdinalIgnoreCase) && !type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)) return false; var charsets = media.Parameters.Where(x => string.Equals(x.Name.Value, "charset", StringComparison.OrdinalIgnoreCase)).ToList(); return charsets.Count is 0 || (charsets.Count == 1 && (string.Equals(charsets[0].Value.Value?.Trim('\"'), "utf-8", StringComparison.OrdinalIgnoreCase) || string.Equals(charsets[0].Value.Value?.Trim('\"'), "utf8", StringComparison.OrdinalIgnoreCase))); }
    private static string MapPath(string? path) { if (string.IsNullOrEmpty(path) || !path.StartsWith("$.", StringComparison.Ordinal)) return "body"; var property = path[2..]; if (property.IndexOfAny(['.', '[', ']', '$']) >= 0) return "body"; return property.ToLowerInvariant() switch { "exerciseid" => "exerciseId", "contenttype" => "contentType", "length" => "length", _ => "body" }; }
    private static IResult Bad(HttpContext context, string field) => Results.Json(new ApiProblemDetails("https://api.trackz.app/problems/validation", "Validation failed", 400, BusinessErrorCode.InvalidRequest, BusinessMessages.Get(BusinessErrorCode.InvalidRequest, CultureInfo.CurrentUICulture, "Request data is invalid."), context.TraceIdentifier, new Dictionary<string, string[]> { [field] = [BusinessMessages.Format("InvalidField", CultureInfo.CurrentUICulture, field)] }), contentType: "application/problem+json", statusCode: 400);
}
