using System.Text.Json;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        { BusinessErrorCode.PasswordPolicyViolation, 10006 },
        { BusinessErrorCode.InvalidRegistrationInput, 10007 },
        { BusinessErrorCode.RateLimitExceeded, 10008 },
        { BusinessErrorCode.InvalidRequest, 10009 },
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
        { BusinessErrorCode.VersionConflict, 60001 },
        { BusinessErrorCode.InternalServerError, 90001 }
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

    [Fact]
    public async Task Business_exception_after_response_started_propagates_without_appending_problem_details()
    {
        var expected = new BusinessException(
            BusinessErrorCode.WorkoutNotFound,
            "Workout session was not found.",
            StatusCodes.Status404NotFound);
        var body = new MemoryStream();
        await body.WriteAsync("partial response"u8.ToArray());
        var responseFeature = new StartedHttpResponseFeature
        {
            StatusCode = StatusCodes.Status202Accepted,
            Body = body
        };
        responseFeature.Headers["X-Downstream"] = "already-sent";
        responseFeature.Headers.ContentType = "text/plain";
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseBodyFeature>(new TestResponseBodyFeature(body));
        var context = new DefaultHttpContext(features);
        var middleware = new BusinessExceptionMiddleware(_ => throw expected);

        Assert.True(context.Response.HasStarted);

        var actual = await Assert.ThrowsAsync<BusinessException>(() => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Equal("text/plain", context.Response.ContentType);
        Assert.Equal("already-sent", context.Response.Headers["X-Downstream"]);
        Assert.Equal("partial response", GetBodyText(body));
    }

    [Fact]
    public async Task Business_exception_replaces_a_dirty_unstarted_response_with_problem_details()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = StatusCodes.Status418ImATeapot;
        context.Response.Headers["X-Downstream"] = "pending";
        context.Response.ContentType = "text/plain";
        context.Response.Body = new MemoryStream();
        await context.Response.Body.WriteAsync("partial response"u8.ToArray());
        var middleware = new BusinessExceptionMiddleware(_ =>
            throw new BusinessException(
                BusinessErrorCode.WorkoutNotFound,
                "Workout session was not found.",
                StatusCodes.Status404NotFound));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("X-Downstream"));
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.DoesNotContain("partial response", GetBodyText(context.Response.Body));
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(30001, document.RootElement.GetProperty("errorCode").GetInt32());
    }

    private static string GetBodyText(Stream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed class StartedHttpResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public string? ReasonPhrase { get; set; }

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }

    private sealed class TestResponseBodyFeature(Stream stream) : IHttpResponseBodyFeature
    {
        public Stream Stream { get; } = stream;

        public PipeWriter Writer { get; } = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));

        public Task CompleteAsync() => Task.CompletedTask;

        public void DisableBuffering()
        {
        }

        public Task SendFileAsync(
            string path,
            long offset,
            long? count,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
