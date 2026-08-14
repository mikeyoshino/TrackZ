using System.Text.Json.Serialization;

namespace TrackZ.Contracts.Errors;

public sealed record ApiProblemDetails(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("errorCode")] BusinessErrorCode ErrorCode,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("traceId")] string TraceId,
    [property: JsonPropertyName("fieldErrors")] IReadOnlyDictionary<string, string[]>? FieldErrors);
