using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Architecture;

public sealed class MauiCompositionTests
{
    [Fact]
    public void Real_Maui_provider_activates_the_routed_logger_graph_with_correct_lifetimes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-composition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton<IConnectivityService>(new HeadlessConnectivity());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            var firstPage = app.Services.GetRequiredService<SetLoggerPage>();
            var secondPage = app.Services.GetRequiredService<SetLoggerPage>();
            var concreteStatus = app.Services.GetRequiredService<OutboxRepository>();
            var interfaceStatus = app.Services.GetRequiredService<IWorkoutOutboxStatusSource>();

            Assert.IsType<SetLoggerViewModel>(firstPage.BindingContext);
            Assert.IsType<SetLoggerViewModel>(secondPage.BindingContext);
            Assert.NotSame(firstPage, secondPage);
            Assert.NotSame(firstPage.BindingContext, secondPage.BindingContext);
            Assert.Same(concreteStatus, interfaceStatus);
            Assert.Same(interfaceStatus, app.Services.GetRequiredService<IWorkoutOutboxStatusSource>());

            var releasedLoggers = Enumerable.Range(0, 8)
                .Select(_ => ResolveAndReleaseLogger(app.Services))
                .ToArray();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.All(releasedLoggers, logger => Assert.False(logger.IsAlive));
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference ResolveAndReleaseLogger(IServiceProvider services)
    {
        var page = services.GetRequiredService<SetLoggerPage>();
        var logger = Assert.IsType<SetLoggerViewModel>(page.BindingContext);
        page.Deactivate();
        return new WeakReference(logger);
    }

    private sealed class HeadlessConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class HeadlessPreferences : IWorkoutPreferenceStore
    {
        public string? Get(string key) => null;
        public void Set(string key, string value) { }
    }

    private sealed class HeadlessDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new HeadlessDispatcher();
    }

    private sealed class HeadlessDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action)
        {
            action();
            return true;
        }
        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }
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
