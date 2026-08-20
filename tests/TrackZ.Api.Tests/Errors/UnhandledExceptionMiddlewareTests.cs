using System.Globalization;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using TrackZ.Api.Middleware;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;

namespace TrackZ.Api.Tests.Errors;

public sealed class UnhandledExceptionMiddlewareTests
{
    [Theory]
    [InlineData("en", "An unexpected error occurred. Please try again later.")]
    [InlineData("th", "เกิดข้อผิดพลาดที่ไม่คาดคิด โปรดลองอีกครั้งในภายหลัง")]
    public async Task Unexpected_exception_returns_safe_localized_problem_details(
        string cultureName,
        string expectedMessage)
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-unexpected-123";
        context.Response.Body = new MemoryStream();
        var culture = CultureInfo.GetCultureInfo(cultureName);
        context.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(new RequestCulture(culture), null));
        var middleware = new UnhandledExceptionMiddleware(_ =>
            throw new InvalidOperationException("access token=secret-token-value"));

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(payload);
        var problem = document.RootElement;

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(90001, problem.GetProperty("errorCode").GetInt32());
        Assert.Equal("Internal server error", problem.GetProperty("title").GetString());
        Assert.Equal(expectedMessage, problem.GetProperty("message").GetString());
        Assert.Equal("trace-unexpected-123", problem.GetProperty("traceId").GetString());
        Assert.Equal(JsonValueKind.Null, problem.GetProperty("fieldErrors").ValueKind);
        Assert.DoesNotContain("secret-token-value", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unexpected_exception_after_response_started_propagates_without_appending_problem_details()
    {
        var expected = new InvalidOperationException("Unexpected failure.");
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
        var middleware = new UnhandledExceptionMiddleware(_ => throw expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(expected, actual);
        Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        Assert.Equal("text/plain", context.Response.ContentType);
        Assert.Equal("already-sent", context.Response.Headers["X-Downstream"]);
        Assert.Equal("partial response", await GetBodyTextAsync(body));
    }

    [Fact]
    public async Task Business_exception_retains_its_specific_problem_details_when_wrapped_by_generic_middleware()
    {
        var expected = new BusinessException(
            BusinessErrorCode.WorkoutNotFound,
            "Workout session was not found.",
            StatusCodes.Status404NotFound);
        var businessMiddleware = new BusinessExceptionMiddleware(_ => throw expected);
        var middleware = new UnhandledExceptionMiddleware(businessMiddleware.InvokeAsync);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(30001, document.RootElement.GetProperty("errorCode").GetInt32());
        Assert.Equal("The workout was not found.", document.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("bad-request")]
    public async Task Non_business_parser_exception_reaches_the_outer_safe_problem_handler(string exceptionKind)
    {
        Exception expected = exceptionKind switch
        {
            "json" => new JsonException("parser secret=do-not-disclose"),
            "bad-request" => new BadHttpRequestException("parser secret=do-not-disclose"),
            _ => throw new ArgumentOutOfRangeException(nameof(exceptionKind))
        };
        var businessMiddleware = new BusinessExceptionMiddleware(_ => throw expected);
        var middleware = new UnhandledExceptionMiddleware(businessMiddleware.InvokeAsync);
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-parser-123";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(payload);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(90001, document.RootElement.GetProperty("errorCode").GetInt32());
        Assert.Equal("trace-parser-123", document.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("parser secret", payload, StringComparison.Ordinal);
    }

    private static async Task<string> GetBodyTextAsync(Stream body)
    {
        body.Position = 0;
        using var reader = new StreamReader(body, leaveOpen: true);
        return await reader.ReadToEndAsync();
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
