using System.Globalization;
using System.Text.Json;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Contracts.Errors;

namespace TrackZ.Api.Middleware;

public sealed class UnhandledExceptionMiddleware(RequestDelegate next)
{
    private const string ProblemType = "https://api.trackz.app/problems/internal-server-error";
    private const string ProblemTitle = "Internal server error";
    private const string FallbackMessage = "An unexpected error occurred. Please try again later.";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            var problem = new ApiProblemDetails(
                ProblemType,
                ProblemTitle,
                StatusCodes.Status500InternalServerError,
                BusinessErrorCode.InternalServerError,
                LocalizeMessage(context),
                context.TraceIdentifier,
                null);

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                problem,
                cancellationToken: context.RequestAborted);
        }
    }

    private static string LocalizeMessage(HttpContext context) =>
        BusinessMessages.Get(
            BusinessErrorCode.InternalServerError,
            context.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()?.RequestCulture.UICulture,
            FallbackMessage);
}
