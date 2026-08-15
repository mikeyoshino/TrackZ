namespace TrackZ.Application.Media;

public enum SignedMediaValidationResult
{
    Valid,
    Expired,
    Invalid
}

public interface IMediaAccessUrlSigner
{
    SignedMediaAccess Create(Guid imageId, string rendition);

    SignedMediaValidationResult Validate(
        Guid imageId,
        string rendition,
        long expiresUnixSeconds,
        string signature);
}

public sealed record SignedMediaAccess(Uri Url, DateTimeOffset ExpiresAt);
