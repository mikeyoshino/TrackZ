using System.Runtime.ExceptionServices;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Sync;
using TrackZ.Mobile.Features.Gamification;

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
    public async Task<LocalExerciseImageSelection?> PickAsync(
        string pickerTitle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pickerTitle);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = pickerTitle,
            FileTypes = FilePickerFileType.Images
        });
        return selected is null ? null : MauiExerciseImageSelection.From(selected);
    }
}

public sealed class MauiLocalExerciseImageCapture : ILocalExerciseImageCapture
{
    public async Task<LocalExerciseImageSelection?> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MediaPicker.Default.IsCaptureSupported) return null;
        var selected = await MediaPicker.Default.CapturePhotoAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return selected is null ? null : MauiExerciseImageSelection.From(selected);
    }
}

internal static class MauiExerciseImageSelection
{
    public static LocalExerciseImageSelection From(FileResult selected) =>
        new(
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
    TrackZLocalDatabase workouts,
    ExerciseHistoryCache history,
    ProgressSnapshotCache progress,
    IExerciseGuidancePreferenceStore guidance) : IMobilePrivateDataCleaner
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

        try
        {
            await history.ClearAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await progress.ClearAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            guidance.Clear();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Private mobile data cleanup failed.", failures);
    }
}

public sealed class MauiSyncAuthenticationRecovery(
    TrackZIdentityRefreshClient identity) : ISyncAuthenticationRecovery
{
    public async Task<bool> TryRecoverAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await identity.RefreshAsync(DeviceInfo.Current.Name, cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is MobileApiException or HttpRequestException or IOException)
        {
            return false;
        }
    }
}
