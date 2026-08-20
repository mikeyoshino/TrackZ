using TrackZ.Mobile.Networking;

namespace TrackZ.Mobile.Tests.Networking;

public sealed class SimulatorApiConfigurationTests
{
    [Fact]
    public void Debug_simulator_origins_use_explicit_clean_http_origins()
    {
        var values = new Dictionary<string, string?>
        {
            ["TRACKZ_API_ORIGIN"] = "http://127.0.0.1:5080",
            ["TRACKZ_MEDIA_ORIGIN"] = "http://127.0.0.1:9000"
        };

        var origins = MobileEndpointOrigins.Resolve(key => values.GetValueOrDefault(key));

        Assert.Equal(new Uri("http://127.0.0.1:5080/"), origins.ApiOrigin);
        Assert.Equal(new Uri("http://127.0.0.1:9000/"), origins.MediaOrigin);
    }

    [Theory]
    [InlineData("https://user@example.com")]
    [InlineData("https://example.com/api")]
    [InlineData("https://example.com/?token=secret")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("ftp://example.com")]
    public void Unsafe_origin_is_rejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MobileEndpointOrigins.Resolve(key =>
                key == "TRACKZ_API_ORIGIN" ? value : "https://media.trackz.app"));
    }
}
