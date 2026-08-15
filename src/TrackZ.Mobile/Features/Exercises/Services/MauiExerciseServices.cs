using System.Runtime.ExceptionServices;
using TrackZ.Mobile.Data;
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

public sealed class MauiLocalExerciseImagePicker : ILocalExerciseImagePicker
{
    public async Task<LocalExerciseImageSelection?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choose an exercise image",
            FileTypes = FilePickerFileType.Images
        });
        if (selected is null) return null;
        return new LocalExerciseImageSelection(
            selected.FileName,
            selected.ContentType,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                var stream = await selected.OpenReadAsync();
                if (!token.IsCancellationRequested) return stream;
                await stream.DisposeAsync();
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException(token);
            });
    }
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

public sealed class MauiPrivateDataCleaner(
    CustomExerciseImageService exercises,
    TrackZLocalDatabase workouts) : IMobilePrivateDataCleaner
{
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<Exception>();
        try
        {
            await exercises.ClearPrivateDataAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await workouts.ClearPrivateDataAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Private mobile data cleanup failed.", failures);
    }
}
