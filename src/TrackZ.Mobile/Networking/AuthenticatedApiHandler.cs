using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Networking;

public sealed class AuthenticatedApiHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly IProtectedRequestAuthentication _authentication;
    private readonly Uri _apiOrigin;
    private readonly IAccountSessionBoundary _sessionBoundary;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private long _refreshEpoch;
    private RefreshAttemptOutcome? _lastRefreshOutcome;

    public AuthenticatedApiHandler(
        IAccessTokenProvider tokenProvider,
        IProtectedRequestAuthentication authentication,
        Uri apiOrigin,
        IAccountSessionBoundary sessionBoundary)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(apiOrigin);
        ArgumentNullException.ThrowIfNull(sessionBoundary);
        if (!apiOrigin.IsAbsoluteUri || apiOrigin.Scheme is not ("http" or "https"))
            throw new ArgumentException("An absolute HTTP API origin is required.", nameof(apiOrigin));
        _tokenProvider = tokenProvider;
        _authentication = authentication;
        _apiOrigin = apiOrigin;
        _sessionBoundary = sessionBoundary;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var isProtectedOrigin = IsApiOrigin(request.RequestUri);
        var requestGeneration = _sessionBoundary.Capture();
        var observedRefreshEpoch = Volatile.Read(ref _refreshEpoch);
        if (isProtectedOrigin)
            _ = await AttachCurrentBearerAsync(request, requestGeneration, cancellationToken);
        else if (string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = null;

        var response = await base.SendAsync(request, cancellationToken);
        if (!isProtectedOrigin
            || response.StatusCode != HttpStatusCode.Unauthorized
            || request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            return response;

        if (_sessionBoundary.IsCancellationRequested(requestGeneration)) return response;

        HttpResponseMessage? finalResponse = null;
        try
        {
            if (!await TryRefreshSingleFlightAsync(
                observedRefreshEpoch, requestGeneration, cancellationToken))
                return response;

            if (_sessionBoundary.IsCancellationRequested(requestGeneration)) return response;

            using var replay = CloneSafeRequest(request);
            if (!await AttachCurrentBearerAsync(replay, requestGeneration, cancellationToken))
                return response;

            response.Dispose();
            finalResponse = await base.SendAsync(replay, cancellationToken);
            if (finalResponse.StatusCode == HttpStatusCode.Unauthorized)
                await _authentication.RequireSignInAsync(requestGeneration, cancellationToken);
            return finalResponse;
        }
        catch
        {
            response.Dispose();
            finalResponse?.Dispose();
            throw;
        }
    }

    private async Task<bool> TryRefreshSingleFlightAsync(
        long observedRefreshEpoch,
        AccountSessionGeneration requestGeneration,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _refreshEpoch) != observedRefreshEpoch)
            {
                var shared = _lastRefreshOutcome;
                if (shared is not null && shared.Generation == requestGeneration)
                    return shared.GetResult();
            }

            RefreshAttemptOutcome outcome;
            try
            {
                outcome = new RefreshAttemptOutcome(
                    requestGeneration,
                    await _authentication.TryRefreshAsync(requestGeneration, cancellationToken),
                    null);
            }
            catch (Exception exception)
            {
                outcome = new RefreshAttemptOutcome(
                    requestGeneration,
                    false,
                    ExceptionDispatchInfo.Capture(exception));
            }
            _lastRefreshOutcome = outcome;
            Interlocked.Increment(ref _refreshEpoch);
            return outcome.GetResult();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private sealed record RefreshAttemptOutcome(
        AccountSessionGeneration Generation,
        bool Succeeded,
        ExceptionDispatchInfo? Failure)
    {
        public bool GetResult()
        {
            Failure?.Throw();
            return Succeeded;
        }
    }

    private async Task<bool> AttachCurrentBearerAsync(
        HttpRequestMessage request,
        AccountSessionGeneration requestGeneration,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = null;
        string? token = null;
        var attached = await _sessionBoundary.TryCommitAsync(requestGeneration, async generationToken =>
        {
            token = await _tokenProvider.GetAccessTokenAsync(generationToken);
            request.Headers.Authorization = string.IsNullOrWhiteSpace(token)
                ? null
                : new AuthenticationHeaderValue("Bearer", token);
        }, cancellationToken);
        return attached;
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
