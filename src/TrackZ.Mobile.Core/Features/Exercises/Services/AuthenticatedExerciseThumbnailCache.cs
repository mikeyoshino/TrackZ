using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using TrackZ.Contracts.Exercises;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class AuthenticatedExerciseThumbnailCache : IExerciseThumbnailCache
{
    private const long MaximumDownloadedBytes = 5_000_000;
    private readonly HttpClient _apiClient;
    private readonly HttpClient _mediaClient;
    private readonly Uri _mediaOrigin;
    private readonly string _cacheDirectory;
    private readonly IClock _clock;
    private readonly IAccountSessionBoundary _sessionBoundary;

    public AuthenticatedExerciseThumbnailCache(
        HttpClient apiClient,
        HttpClient mediaClient,
        Uri mediaOrigin,
        string cacheDirectory,
        IClock clock,
        IAccountSessionBoundary sessionBoundary)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(mediaClient);
        ArgumentNullException.ThrowIfNull(mediaOrigin);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(sessionBoundary);
        if (!IsCleanOrigin(mediaOrigin))
            throw new ArgumentException("The media origin must be a clean HTTP(S) origin.", nameof(mediaOrigin));
        if (mediaClient.DefaultRequestHeaders.Authorization is not null)
            throw new ArgumentException("The signed-media client must not have authorization defaults.", nameof(mediaClient));

        _apiClient = apiClient;
        _mediaClient = mediaClient;
        _mediaOrigin = mediaOrigin;
        _cacheDirectory = cacheDirectory;
        _clock = clock;
        _sessionBoundary = sessionBoundary;
    }

    public async Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUri)) return null;
        if (!TryParseCanonicalMediaRoute(thumbnailUri, out var imageId))
            throw new InvalidDataException("Exercise thumbnail URLs must be canonical API routes.");
        var generation = _sessionBoundary.Capture();
        Directory.CreateDirectory(_cacheDirectory);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(thumbnailUri))).ToLowerInvariant();
        var basePath = Path.Combine(_cacheDirectory, name);
        var existing = new[] { ".jpg", ".png", ".webp" }
            .Select(extension => basePath + extension)
            .FirstOrDefault(File.Exists);
        if (existing is not null) return existing;

        var temporary = basePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            for (var accessAttempt = 0; accessAttempt < 2; accessAttempt++)
            {
                var signedUrl = await AuthorizeAsync(thumbnailUri, imageId, cancellationToken);
                if (_mediaClient.DefaultRequestHeaders.Authorization is not null)
                    throw new InvalidOperationException("The signed-media client cannot carry API authorization.");
                using var request = new HttpRequestMessage(HttpMethod.Get, signedUrl);
                request.Headers.Authorization = null;
                using var response = await _mediaClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (accessAttempt == 0 && response.StatusCode is HttpStatusCode.Gone or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    continue;

                response.EnsureSuccessStatusCode();
                var extension = response.Content.Headers.ContentType?.MediaType switch
                {
                    "image/jpeg" => ".jpg",
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => null
                };
                if (extension is null)
                    throw new InvalidDataException("The signed media response is not a supported image.");
                if (response.Content.Headers.ContentLength is > MaximumDownloadedBytes)
                    throw new InvalidDataException("The signed media response is too large.");
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var target = File.Create(temporary))
                    await CopyWithLimitAsync(source, target, cancellationToken);
                var destination = basePath + extension;
                var promoted = await _sessionBoundary.TryCommitAsync(generation, _ =>
                {
                    File.Move(temporary, destination, overwrite: true);
                    return Task.CompletedTask;
                }, cancellationToken);
                return promoted ? destination : null;
            }
            throw new HttpRequestException("The signed media authorization expired twice.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_cacheDirectory)) return Task.CompletedTask;
        foreach (var path in Directory.EnumerateFiles(_cacheDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private async Task<Uri> AuthorizeAsync(
        string authorizationRoute,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        using var response = await _apiClient.GetAsync(
            authorizationRoute,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        SignedMediaAccessDto? access;
        try
        {
            access = await response.Content.ReadFromJsonAsync<SignedMediaAccessDto>(cancellationToken);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvalidDataException("The media authorization response is invalid.", exception);
        }
        if (access is null || !TryValidateSignedUrl(access, imageId, out var signedUrl))
            throw new InvalidDataException("The media authorization response is invalid.");
        return signedUrl;
    }

    private bool TryValidateSignedUrl(SignedMediaAccessDto access, Guid expectedImageId, out Uri signedUrl)
    {
        signedUrl = null!;
        if (string.IsNullOrWhiteSpace(access.Url)
            || access.Url.Contains('\\')
            || access.Url.Contains('%')
            || access.Url.Contains('#')
            || !Uri.TryCreate(access.Url, UriKind.Absolute, out var parsed)
            || !SameOrigin(parsed, _mediaOrigin)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.AbsolutePath != $"/media/v1/exercise-images/{expectedImageId:D}/thumbnail") return false;

        var query = parsed.Query;
        var parts = query.Length > 1 ? query[1..].Split('&') : [];
        if (parts.Length != 2
            || !parts[0].StartsWith("expires=", StringComparison.Ordinal)
            || !parts[1].StartsWith("signature=", StringComparison.Ordinal)
            || !long.TryParse(parts[0][8..], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || parts[1][10..] is not { Length: 43 } signature
            || signature.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;

        DateTimeOffset queryExpiry;
        try { queryExpiry = DateTimeOffset.FromUnixTimeSeconds(expires); }
        catch (ArgumentOutOfRangeException) { return false; }
        var now = _clock.UtcNow;
        if (access.ExpiresAt.ToUniversalTime() != queryExpiry
            || queryExpiry <= now
            || queryExpiry > now.AddMinutes(5)) return false;
        signedUrl = parsed;
        return true;
    }

    private static bool IsCleanOrigin(Uri origin) =>
        origin.IsAbsoluteUri
        && origin.Scheme is "https" or "http"
        && string.IsNullOrEmpty(origin.UserInfo)
        && string.IsNullOrEmpty(origin.Query)
        && string.IsNullOrEmpty(origin.Fragment)
        && origin.AbsolutePath == "/";

    private static bool SameOrigin(Uri candidate, Uri configured) =>
        string.Equals(candidate.Scheme, configured.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.IdnHost, configured.IdnHost, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == configured.Port;

    private static bool TryParseCanonicalMediaRoute(string route, out Guid imageId)
    {
        imageId = Guid.Empty;
        if (!route.StartsWith("/api/v1/media/exercise-images/", StringComparison.Ordinal)
            || route.Contains('\\')
            || route.Contains('%')
            || route.Contains('?')
            || route.Contains('#')) return false;
        var segments = route.Split('/');
        return segments is ["", "api", "v1", "media", "exercise-images", var value, "thumbnail"]
            && Guid.TryParseExact(value, "D", out imageId)
            && imageId != Guid.Empty;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            copied += read;
            if (copied > MaximumDownloadedBytes)
                throw new InvalidDataException("The signed media response is too large.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (copied == 0)
            throw new InvalidDataException("The signed media response is empty.");
    }
}
