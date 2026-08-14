using TrackZ.Contracts.Errors;

namespace TrackZ.Application.Common.Exceptions;

public sealed class BusinessException(
    BusinessErrorCode code,
    string message,
    int statusCode) : Exception(message)
{
    public BusinessErrorCode Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
