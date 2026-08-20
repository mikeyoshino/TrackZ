using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile.Networking;

public sealed record MobileEndpointOrigins(Uri ApiOrigin, Uri MediaOrigin)
{
    public const string ApiOriginEnvironmentVariable = "TRACKZ_API_ORIGIN";
    public const string MediaOriginEnvironmentVariable = "TRACKZ_MEDIA_ORIGIN";

    private static readonly Uri ProductionApiOrigin = new("https://api.trackz.app");
    private static readonly Uri ProductionMediaOrigin = new("https://media.trackz.app");

    public static MobileEndpointOrigins Resolve(Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        var configuredApi = readEnvironment(ApiOriginEnvironmentVariable);
        var hasApiOverride = !string.IsNullOrWhiteSpace(configuredApi);
        var api = !hasApiOverride
            ? ProductionApiOrigin
            : ParseOrigin(configuredApi!, ApiOriginEnvironmentVariable);
        var configuredMedia = readEnvironment(MediaOriginEnvironmentVariable);
        var media = string.IsNullOrWhiteSpace(configuredMedia)
            ? hasApiOverride ? api : ProductionMediaOrigin
            : ParseOrigin(configuredMedia, MediaOriginEnvironmentVariable);
        return new MobileEndpointOrigins(api, media);
    }

    private static Uri ParseOrigin(string value, string setting)
    {
        var normalized = value.Trim();
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var origin)
            || (!string.Equals(origin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(origin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            || string.IsNullOrWhiteSpace(origin.Host)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new InvalidOperationException($"{setting} must be a clean HTTP(S) origin.");
        }

        return origin;
    }
}

public sealed class DevelopmentAccessTokenProvider(
    IAccessTokenProvider secureStorage,
    Func<string?> readFallbackToken) : IAccessTokenProvider
{
    public const string AccessTokenEnvironmentVariable = "TRACKZ_SIMULATOR_ACCESS_TOKEN";

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var stored = await secureStorage.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored)) return stored;
        cancellationToken.ThrowIfCancellationRequested();
        var fallback = readFallbackToken();
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }
}
