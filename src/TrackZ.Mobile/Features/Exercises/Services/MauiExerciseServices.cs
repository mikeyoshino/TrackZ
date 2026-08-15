using TrackZ.Mobile.Identity;

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

public sealed class SecureMobileTokenStorage : IMobileTokenStorage
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SecureStorage.Default.GetAsync(key);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(key, value);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class MauiPrivateDataCleaner(CustomExerciseImageService exercises) : IMobilePrivateDataCleaner
{
    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        exercises.ClearPrivateDataAsync(cancellationToken);
}
