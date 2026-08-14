namespace TrackZ.Application.Identity.Common;

public sealed record AuthTokenPair(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
