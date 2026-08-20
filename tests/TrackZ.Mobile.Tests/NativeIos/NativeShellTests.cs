using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class NativeShellTests
{
    [Fact]
    public void Authenticated_shell_exposes_exactly_four_native_tabs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-native-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        using var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton(new ProgressSnapshotCache(Path.Combine(root, "progress.json")));
            services.AddSingleton<IExerciseThumbnailCache, NullThumbnailCache>();
            services.AddSingleton<IConnectivityService, OfflineConnectivity>();
            services.AddSingleton<IWorkoutPreferenceStore, MemoryPreferences>();
        });

        try
        {
            var shell = app.Services.GetRequiredService<AppShell>();
            var routes = shell.Items
                .SelectMany(item => item.Items)
                .SelectMany(section => section.Items)
                .Select(content => content.Route)
                .ToArray();

            Assert.Equal(["train", "history", "progress", "you"], routes);
            Assert.DoesNotContain("exercises", routes);
        }
        finally
        {
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NullThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(
            string? thumbnailUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MemoryPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class HeadlessDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new HeadlessDispatcher();
    }

    private sealed class HeadlessDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new HeadlessTimer();
    }

    private sealed class HeadlessTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
