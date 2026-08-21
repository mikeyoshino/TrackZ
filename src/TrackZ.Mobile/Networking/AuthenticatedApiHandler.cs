using System.Net;
using System.Net.Http.Headers;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Networking;

public sealed class AuthenticatedApiHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly IProtectedRequestAuthentication _authentication;
    private readonly Uri _apiOrigin;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private long _refreshEpoch;
    private bool _lastRefreshSucceeded;

    public AuthenticatedApiHandler(
        IAccessTokenProvider tokenProvider,
        IProtectedRequestAuthentication authentication,
        Uri apiOrigin)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(apiOrigin);
        if (!apiOrigin.IsAbsoluteUri || apiOrigin.Scheme is not ("http" or "https"))
            throw new ArgumentException("An absolute HTTP API origin is required.", nameof(apiOrigin));
        _tokenProvider = tokenProvider;
        _authentication = authentication;
        _apiOrigin = apiOrigin;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isProtectedOrigin = IsApiOrigin(request.RequestUri);
        var observedRefreshEpoch = Volatile.Read(ref _refreshEpoch);
        if (isProtectedOrigin)
            await AttachCurrentBearerAsync(request, cancellationToken);
        else if (string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = null;

        var response = await base.SendAsync(request, cancellationToken);
        if (!isProtectedOrigin
            || response.StatusCode != HttpStatusCode.Unauthorized
            || request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return response;

        response.Dispose();
        if (!await TryRefreshSingleFlightAsync(observedRefreshEpoch, cancellationToken))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized) { RequestMessage = request };

        using var replay = CloneSafeRequest(request);
        await AttachCurrentBearerAsync(replay, cancellationToken);
        var finalResponse = await base.SendAsync(replay, cancellationToken);
        if (finalResponse.StatusCode == HttpStatusCode.Unauthorized)
            await _authentication.RequireSignInAsync(cancellationToken);
        return finalResponse;
    }

    private async Task<bool> TryRefreshSingleFlightAsync(
        long observedRefreshEpoch,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _refreshEpoch) != observedRefreshEpoch)
                return _lastRefreshSucceeded;
            _lastRefreshSucceeded = await _authentication.TryRefreshAsync(cancellationToken);
            Interlocked.Increment(ref _refreshEpoch);
            return _lastRefreshSucceeded;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task AttachCurrentBearerAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    private static HttpRequestMessage CloneSafeRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        return clone;
    }

    private bool IsApiOrigin(Uri? requestUri)
    {
        if (requestUri is not { IsAbsoluteUri: true }
            || !string.Equals(requestUri.Scheme, _apiOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requestUri.IdnHost, _apiOrigin.IdnHost, StringComparison.OrdinalIgnoreCase)
            || requestUri.Port != _apiOrigin.Port)
            return false;

        var root = _apiOrigin.AbsolutePath;
        if (root == "/") return requestUri.AbsolutePath.StartsWith("/", StringComparison.Ordinal);
        var normalizedRoot = root.EndsWith('/') ? root : root + "/";
        return string.Equals(requestUri.AbsolutePath, root.TrimEnd('/'), StringComparison.Ordinal)
            || requestUri.AbsolutePath.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }
}
