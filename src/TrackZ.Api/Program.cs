using TrackZ.Application;
using TrackZ.Api.Endpoints;
using TrackZ.Api.Middleware;
using TrackZ.Infrastructure;
using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using TrackZ.Contracts.Errors;
using TrackZ.Api;

var catalogDeploymentCommand = ExerciseCatalogDeploymentCommand.Parse(args);
var builder = WebApplication.CreateBuilder(catalogDeploymentCommand is null ? args : []);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TrackZ.Application.Exercises.ListExercises.ICurrentUser, HttpCurrentUser>();
builder.Services.AddAuthorization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var english = CultureInfo.GetCultureInfo("en");
    var thai = CultureInfo.GetCultureInfo("th");
    options.DefaultRequestCulture = new RequestCulture(english);
    options.SupportedCultures = [english, thai];
    options.SupportedUICultures = [english, thai];
    options.RequestCultureProviders = [new CustomRequestCultureProvider(context =>
    {
        var supported = context.Request.GetTypedHeaders().AcceptLanguage?
            .Where(header => (header.Quality ?? 1d) > 0d)
            .OrderByDescending(header => header.Quality ?? 1d)
            .Select(header => header.Value.Value ?? string.Empty)
            .Select(language => language.Split('-', 2)[0])
            .FirstOrDefault(language => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                || string.Equals(language, "th", StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<ProviderCultureResult?>(supported is null
            ? null
            : new ProviderCultureResult(supported, supported));
    })];
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.ContentType = "application/problem+json";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await JsonSerializer.SerializeAsync(response.Body, new ApiProblemDetails(
            "https://api.trackz.app/problems/rate-limit-exceeded",
            "Too many requests",
            StatusCodes.Status429TooManyRequests,
            BusinessErrorCode.RateLimitExceeded,
            BusinessMessages.Get(BusinessErrorCode.RateLimitExceeded, CultureInfo.CurrentUICulture, "Too many requests. Please try again later."),
            context.HttpContext.TraceIdentifier,
            null), cancellationToken: cancellationToken);
    };
    options.AddPolicy("identity", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Connection.RemoteIpAddress}:{context.Request.Path}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();

if (catalogDeploymentCommand is not null)
{
    await catalogDeploymentCommand.ExecuteAsync(app.Services);
    return;
}

app.UseRequestLocalization();
app.UseMiddleware<UnhandledExceptionMiddleware>();
app.UseMiddleware<BusinessExceptionMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentityEndpoints();
app.MapExerciseEndpoints();
app.MapMediaEndpoints();

app.Run();

public partial class Program;
