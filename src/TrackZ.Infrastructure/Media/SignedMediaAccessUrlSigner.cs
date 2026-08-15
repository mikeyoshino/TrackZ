using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TrackZ.Application.Media;

namespace TrackZ.Infrastructure.Media;

public sealed class MediaAccessOptions
{
    public const string SectionName = "MediaAccess";

    [Required]
    public string PublicOrigin { get; init; } = string.Empty;

    [Required, MinLength(32)]
    public string SigningKey { get; init; } = string.Empty;

    [Range(15, 300)]
    public int LifetimeSeconds { get; init; } = 60;

    public bool IsValid()
    {
        if (SigningKey.Length < 32 || LifetimeSeconds is < 15 or > 300 ||
            !Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin)) return false;
        return origin.Scheme is "https" or "http"
            && string.IsNullOrEmpty(origin.UserInfo)
            && string.IsNullOrEmpty(origin.Query)
            && string.IsNullOrEmpty(origin.Fragment)
            && origin.AbsolutePath == "/";
    }
}

public sealed class SignedMediaAccessUrlSigner(
    IOptions<MediaAccessOptions> options,
    TimeProvider timeProvider) : IMediaAccessUrlSigner
{
    private readonly MediaAccessOptions _options = options.Value.IsValid()
        ? options.Value
        : throw new InvalidOperationException("Media access signing settings are invalid.");

    public SignedMediaAccess Create(Guid imageId, string rendition)
    {
        ValidateInputs(imageId, rendition);
        var expires = timeProvider.GetUtcNow().AddSeconds(_options.LifetimeSeconds).ToUnixTimeSeconds();
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expires);
        var signature = Sign(imageId, rendition, expires);
        var url = $"{_options.PublicOrigin.TrimEnd('/')}/media/v1/exercise-images/{imageId:D}/{rendition}" +
            $"?expires={expires.ToString(CultureInfo.InvariantCulture)}&signature={Uri.EscapeDataString(signature)}";
        return new SignedMediaAccess(new Uri(url, UriKind.Absolute), expiresAt);
    }

    public SignedMediaValidationResult Validate(
        Guid imageId,
        string rendition,
        long expiresUnixSeconds,
        string signature)
    {
        if (imageId == Guid.Empty || rendition is not ("master" or "thumbnail") ||
            expiresUnixSeconds <= 0 || string.IsNullOrWhiteSpace(signature))
            return SignedMediaValidationResult.Invalid;

        var expected = Encoding.ASCII.GetBytes(Sign(imageId, rendition, expiresUnixSeconds));
        var provided = Encoding.ASCII.GetBytes(signature);
        if (provided.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(provided, expected))
            return SignedMediaValidationResult.Invalid;

        DateTimeOffset expiresAt;
        try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnixSeconds); }
        catch (ArgumentOutOfRangeException) { return SignedMediaValidationResult.Invalid; }
        var now = timeProvider.GetUtcNow();
        if (expiresAt <= now) return SignedMediaValidationResult.Expired;
        return expiresAt > now.AddSeconds(_options.LifetimeSeconds + 1)
            ? SignedMediaValidationResult.Invalid
            : SignedMediaValidationResult.Valid;
    }

    private string Sign(Guid imageId, string rendition, long expiresUnixSeconds)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"v1\n{imageId:D}\n{rendition}\n{expiresUnixSeconds.ToString(CultureInfo.InvariantCulture)}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningKey));
        return Convert.ToBase64String(hmac.ComputeHash(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void ValidateInputs(Guid imageId, string rendition)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(imageId, Guid.Empty);
        if (rendition is not ("master" or "thumbnail"))
            throw new ArgumentOutOfRangeException(nameof(rendition));
    }
}
