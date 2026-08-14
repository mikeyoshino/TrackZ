using System.Globalization;
using System.Resources;
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
                LocalizeMessage(context, exception),
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
        catch (BadHttpRequestException) when (!context.Response.HasStarted)
        {
            await WriteValidationProblemAsync(context);
        }
        catch (JsonException) when (!context.Response.HasStarted)
        {
            await WriteValidationProblemAsync(context);
        }
    }

    private static string LocalizeMessage(HttpContext context, BusinessException exception) =>
        BusinessMessages.Get(exception.Code, context.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()?.RequestCulture.UICulture, exception.Message);

    private static async Task WriteValidationProblemAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new ApiProblemDetails(
            "https://api.trackz.app/problems/validation", "Validation failed", StatusCodes.Status400BadRequest,
            BusinessErrorCode.InvalidRequest,
            BusinessMessages.Get(BusinessErrorCode.InvalidRequest, CultureInfo.CurrentUICulture, "Request data is invalid."),
            context.TraceIdentifier,
            new Dictionary<string, string[]> { ["body"] = [BusinessMessages.Get(BusinessErrorCode.InvalidRequest, CultureInfo.CurrentUICulture, "Request data is invalid.")] }),
            cancellationToken: context.RequestAborted);
    }
}

internal static class BusinessMessages
{
    private static readonly ResourceManager ResourceManager = new("TrackZ.Api.Resources.BusinessMessages", typeof(BusinessMessages).Assembly);

    public static string Get(BusinessErrorCode code, CultureInfo? culture, string fallback) =>
        ResourceManager.GetString(code.ToString(), culture ?? CultureInfo.GetCultureInfo("en")) ?? fallback;

    public static string Format(string key, CultureInfo? culture, params object[] arguments) =>
        string.Format(culture ?? CultureInfo.GetCultureInfo("en"), ResourceManager.GetString(key, culture ?? CultureInfo.GetCultureInfo("en")) ?? key, arguments);
}
