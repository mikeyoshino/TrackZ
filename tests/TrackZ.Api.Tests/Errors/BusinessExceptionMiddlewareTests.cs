using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TrackZ.Api.Middleware;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;

namespace TrackZ.Api.Tests.Errors;

public sealed class BusinessExceptionMiddlewareTests
{
    public static TheoryData<BusinessErrorCode, int> PublishedCodes => new()
    {
        { BusinessErrorCode.InvalidCredentials, 10001 },
        { BusinessErrorCode.EmailAlreadyExists, 10002 },
        { BusinessErrorCode.RefreshTokenInvalid, 10003 },
        { BusinessErrorCode.EmailVerificationInvalid, 10004 },
        { BusinessErrorCode.PasswordResetInvalid, 10005 },
        { BusinessErrorCode.ExerciseNotFound, 20001 },
        { BusinessErrorCode.ExerciseNameDuplicate, 20002 },
        { BusinessErrorCode.WorkoutNotFound, 30001 },
        { BusinessErrorCode.WorkoutAlreadyCompleted, 30002 },
        { BusinessErrorCode.InvalidSetValue, 30004 },
        { BusinessErrorCode.ImageTooLarge, 50002 },
        { BusinessErrorCode.ImageTypeNotSupported, 50003 },
        { BusinessErrorCode.VersionConflict, 60001 }
    };

    [Theory]
    [MemberData(nameof(PublishedCodes))]
    public void Published_business_error_code_has_its_immutable_numeric_value(
        BusinessErrorCode code,
        int expectedValue)
    {
        Assert.Equal(expectedValue, (int)code);
    }

    [Fact]
    public async Task Business_exception_returns_stable_problem_details()
    {
        var middleware = new BusinessExceptionMiddleware(_ =>
            throw new BusinessException(
                BusinessErrorCode.WorkoutNotFound,
                "Workout session was not found.",
                StatusCodes.Status404NotFound));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-contract-123";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(30001, root.GetProperty("errorCode").GetInt32());
        Assert.Equal("Workout session was not found.", root.GetProperty("message").GetString());
        Assert.Equal("trace-contract-123", root.GetProperty("traceId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fieldErrors").ValueKind);
        Assert.Equal(StatusCodes.Status404NotFound, root.GetProperty("status").GetInt32());
        Assert.Equal("Business rule violation", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("ErrorCode", out _));
    }

    [Fact]
    public async Task Unexpected_exception_propagates_without_a_business_response()
    {
        var expected = new InvalidOperationException("Unexpected failure.");
        var middleware = new BusinessExceptionMiddleware(_ => throw expected);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(0, context.Response.Body.Length);
    }
}
