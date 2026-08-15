using System.Net.Http.Headers;

namespace TrackZ.Mobile.Features.Exercises.Services;

public interface IAccessTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly Uri _apiOrigin;

    public BearerTokenHandler(IAccessTokenProvider tokenProvider, Uri apiOrigin)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(apiOrigin);
        if (!apiOrigin.IsAbsoluteUri || apiOrigin.Scheme is not ("http" or "https"))
            throw new ArgumentException("An absolute HTTP API origin is required.", nameof(apiOrigin));
        _tokenProvider = tokenProvider;
        _apiOrigin = apiOrigin;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null && IsApiOrigin(request.RequestUri))
        {
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await base.SendAsync(request, cancellationToken);
    }

    private bool IsApiOrigin(Uri? requestUri) =>
        requestUri is { IsAbsoluteUri: true }
        && string.Equals(requestUri.Scheme, _apiOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(requestUri.IdnHost, _apiOrigin.IdnHost, StringComparison.OrdinalIgnoreCase)
        && requestUri.Port == _apiOrigin.Port;
}
