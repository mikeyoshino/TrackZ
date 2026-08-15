namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class SignedMediaDownloadClient(HttpClient httpClient) : IDisposable
{
    public HttpClient HttpClient { get; } = httpClient;

    public void Dispose() => HttpClient.Dispose();
}
