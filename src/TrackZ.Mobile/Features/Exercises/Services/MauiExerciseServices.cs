namespace TrackZ.Mobile.Features.Exercises.Services;

public sealed class MauiConnectivityService : IConnectivityService, IDisposable
{
    public MauiConnectivityService() => Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    public event EventHandler? ConnectivityChanged;

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs eventArgs) =>
        ConnectivityChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);
}

public sealed class SecureStorageAccessTokenProvider : IAccessTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.GetAsync("trackz_access_token");
    }
}
