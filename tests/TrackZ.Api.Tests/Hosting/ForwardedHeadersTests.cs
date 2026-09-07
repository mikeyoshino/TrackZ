using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TrackZ.Api.Tests.Hosting;

public sealed class ForwardedHeadersTests
{
    [Fact]
    public void Production_host_trusts_exactly_one_configured_proxy_hop()
    {
        using var factory = new ProxyConfigurationFactory();

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Equal([IPAddress.Parse("172.30.0.2")], options.KnownProxies);
    }

    private sealed class ProxyConfigurationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TrackZ", "Host=127.0.0.1;Port=1;Database=trackz;Username=trackz;Password=not-used");
            builder.UseSetting("Jwt:Issuer", "trackz-api");
            builder.UseSetting("Jwt:Audience", "trackz-mobile");
            builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-at-least-thirty-two-bytes-long");
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "14");
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ReverseProxy:KnownProxy"] = "172.30.0.2"
                }));
            builder.ConfigureServices(services => services.ReplaceStagingLifecycleWithNoOpForTests());
        }
    }
}
