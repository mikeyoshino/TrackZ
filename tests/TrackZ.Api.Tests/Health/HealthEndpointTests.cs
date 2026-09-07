using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Api.Tests.Health;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Liveness_is_anonymous_and_reports_only_process_status()
    {
        await using var factory = new HealthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"status\":\"Healthy\"}", body);
    }

    [Fact]
    public async Task Readiness_is_unavailable_when_required_dependencies_cannot_be_reached()
    {
        await using var factory = new HealthApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"status\":\"Unhealthy\"}", body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HealthApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TrackZ", "Host=127.0.0.1;Port=1;Database=trackz;Username=trackz;Password=not-used;Timeout=1;Command Timeout=1");
            builder.UseSetting("Jwt:Issuer", "trackz-api");
            builder.UseSetting("Jwt:Audience", "trackz-mobile");
            builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "14");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ObjectStorage:ServiceUrl"] = "http://127.0.0.1:1",
                    ["ObjectStorage:Bucket"] = "trackz-test-private",
                    ["ObjectStorage:AccessKey"] = "test-access-key",
                    ["ObjectStorage:SecretKey"] = "test-secret-key-not-a-real-secret",
                    ["MediaAccess:PublicOrigin"] = "https://media.trackz.test",
                    ["MediaAccess:SigningKey"] = "test-media-access-signing-key-at-least-32-characters",
                    ["MediaAccess:LifetimeSeconds"] = "60",
                    ["ReverseProxy:KnownProxy"] = "172.30.0.2"
                }));
            builder.ConfigureServices(services => services.ReplaceStagingLifecycleWithNoOpForTests());
        }
    }
}
