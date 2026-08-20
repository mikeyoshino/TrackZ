namespace TrackZ.Mobile.Networking;

public sealed record MobileEndpointOriginSet(Uri ApiOrigin, Uri MediaOrigin);

public static class MobileEndpointOrigins
{
    private static readonly Uri DefaultApiOrigin = new("https://api.trackz.app/");
    private static readonly Uri DefaultMediaOrigin = new("https://media.trackz.app/");

    public static MobileEndpointOriginSet Resolve(Func<string, string?> readSetting)
    {
        ArgumentNullException.ThrowIfNull(readSetting);
        return new MobileEndpointOriginSet(
            Parse(readSetting("TRACKZ_API_ORIGIN"), DefaultApiOrigin, "TRACKZ_API_ORIGIN"),
            Parse(readSetting("TRACKZ_MEDIA_ORIGIN"), DefaultMediaOrigin, "TRACKZ_MEDIA_ORIGIN"));
    }

    private static Uri Parse(string? value, Uri fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(origin.Host)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath != "/")
            throw new InvalidOperationException($"{name} must be a clean HTTP(S) origin.");
        return origin;
    }
}
