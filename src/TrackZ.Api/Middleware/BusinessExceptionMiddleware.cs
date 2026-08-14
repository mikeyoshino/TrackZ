using System.Text.Json;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;

namespace TrackZ.Api.Middleware;

public sealed class BusinessExceptionMiddleware(RequestDelegate next)
{
    private const string ProblemType = "https://api.trackz.app/problems/business-rule-violation";
    private const string ProblemTitle = "Business rule violation";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BusinessException exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            var problem = new ApiProblemDetails(
                ProblemType,
                ProblemTitle,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                context.TraceIdentifier,
                null);

            context.Response.Clear();
            context.Response.StatusCode = exception.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                problem,
                cancellationToken: context.RequestAborted);
        }
    }
}
