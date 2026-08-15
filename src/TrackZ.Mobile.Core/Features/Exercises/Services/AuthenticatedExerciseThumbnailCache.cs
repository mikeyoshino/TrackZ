using System.Security.Cryptography;
using System.Text;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class AuthenticatedExerciseThumbnailCache : IExerciseThumbnailCache
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly IAccountSessionBoundary _sessionBoundary;

    public AuthenticatedExerciseThumbnailCache(HttpClient httpClient, string cacheDirectory)
        : this(httpClient, cacheDirectory, new AccountSessionBoundary())
    {
    }

    public AuthenticatedExerciseThumbnailCache(
        HttpClient httpClient,
        string cacheDirectory,
        IAccountSessionBoundary sessionBoundary)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _sessionBoundary = sessionBoundary;
    }

    public async Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUri)) return null;
        if (!IsCanonicalMediaRoute(thumbnailUri))
            throw new InvalidDataException("Exercise thumbnail URLs must be same-origin relative routes.");
        var generation = _sessionBoundary.Capture();
        Directory.CreateDirectory(_cacheDirectory);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(thumbnailUri))).ToLowerInvariant() + ".jpg";
        var destination = Path.Combine(_cacheDirectory, name);
        if (File.Exists(destination)) return destination;

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var response = await _httpClient.GetAsync(thumbnailUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = File.Create(temporary))
                await source.CopyToAsync(target, cancellationToken);
            var promoted = await _sessionBoundary.TryCommitAsync(generation, _ =>
            {
                File.Move(temporary, destination, overwrite: true);
                return Task.CompletedTask;
            }, cancellationToken);
            return promoted ? destination : null;
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

    private static bool IsCanonicalMediaRoute(string route)
    {
        if (!route.StartsWith("/api/v1/media/exercise-images/", StringComparison.Ordinal)
            || route.Contains('\\')
            || route.Contains('%')
            || route.Contains('?')
            || route.Contains('#')) return false;
        var segments = route.Split('/');
        return segments is ["", "api", "v1", "media", "exercise-images", var imageId, "thumbnail"]
            && Guid.TryParseExact(imageId, "D", out var parsed)
            && parsed != Guid.Empty;
    }
}
