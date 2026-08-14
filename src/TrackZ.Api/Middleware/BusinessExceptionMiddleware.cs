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
    }

    private static string LocalizeMessage(HttpContext context, BusinessException exception)
    {
        var language = context.Request.GetTypedHeaders().AcceptLanguage?
            .OrderByDescending(header => header.Quality ?? 1)
            .Select(header => header.Value.Value)
            .FirstOrDefault();

        if (language is null || (!string.Equals(language, "th", StringComparison.OrdinalIgnoreCase)
            && !language.StartsWith("th-", StringComparison.OrdinalIgnoreCase)))
        {
            return exception.Message;
        }

        return exception.Code switch
        {
            BusinessErrorCode.InvalidCredentials => "อีเมลหรือรหัสผ่านไม่ถูกต้อง",
            BusinessErrorCode.EmailAlreadyExists => "มีบัญชีที่ใช้อีเมลนี้อยู่แล้ว",
            BusinessErrorCode.PasswordPolicyViolation => "รหัสผ่านต้องมีอย่างน้อย 12 อักขระ และประกอบด้วยตัวพิมพ์ใหญ่ ตัวพิมพ์เล็ก ตัวเลข และสัญลักษณ์",
            BusinessErrorCode.InvalidRegistrationInput => "ข้อมูลการลงทะเบียนไม่ถูกต้อง",
            _ => exception.Message
        };
    }
}
