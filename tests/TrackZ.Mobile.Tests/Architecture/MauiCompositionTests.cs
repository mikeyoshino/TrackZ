using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Sync;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Architecture;

public sealed class MauiCompositionTests
{
    [Fact]
    public async Task Real_Maui_provider_activates_the_routed_logger_graph_with_correct_lifetimes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-composition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        var preferences = new HeadlessPreferences();
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton<IConnectivityService>(new HeadlessConnectivity());
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(preferences);
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

            await app.Services.GetRequiredService<ExerciseCache>().ReplaceAllAsync([
                new ExerciseSummaryDto(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Composed Press",
                    BodyPart.Chest,
                    TrackingMode.Weighted,
                    null,
                    DateTimeOffset.UtcNow,
                    new PerformanceSetDto(70.125m, null, 8),
                    new PerformanceSetDto(70.125m, null, 5),
                    false),
                new ExerciseSummaryDto(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Composed Pull-up",
                    BodyPart.Back,
                    TrackingMode.Assisted,
                    null,
                    DateTimeOffset.UtcNow,
                    new PerformanceSetDto(null, 25.125m, 10),
                    new PerformanceSetDto(null, 25.125m, 8),
                    false)
            ], DateTimeOffset.UtcNow);
            app.Services.GetRequiredService<IWeightUnitPreference>().Set(WeightDisplayUnit.Pounds);
            var picker = app.Services.GetRequiredService<ExercisePickerViewModel>();
            await picker.LoadAsync();

            Assert.Equal("LAST  154.60 lb × 8", picker.Exercises.Single(item => item.TrackingMode == TrackingMode.Weighted).LastText);
            Assert.Equal("LAST  55.39 lb assist × 10", picker.Exercises.Single(item => item.TrackingMode == TrackingMode.Assisted).LastText);

            var releasedLoggers = Enumerable.Range(0, 8)
                .Select(_ => ResolveAndReleaseLogger(app.Services))
                .ToArray();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.All(releasedLoggers, logger => Assert.False(logger.IsAlive));

            await AssertPulseStartIsAtomicWithResetAsync(app.Services);
            await AssertRunningPulseIsCancelledPromptlyAsync(app.Services);
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertPulseStartIsAtomicWithResetAsync(IServiceProvider services)
    {
        var boundary = services.GetRequiredService<IAccountSessionBoundary>();
        var generation = boundary.Capture();
        using var lease = boundary.CreateCancellationLease(generation);
        var session = new SetSavedFeedbackSession(
            lease.Token,
            phase => boundary.TryStartSessionPhase(generation, phase, lease.Token));
        var feedback = new MauiSetSavedFeedback(() => false);
        var pulse = new GatedPulseDriver(blockBeforeStart: true);
        var page = new TestSetLoggerPage(
            services.GetRequiredService<SetLoggerViewModel>(), feedback, pulse);
        page.Appear();

        var running = feedback.SetSavedAsync(new LocalSet(50m, null, 10), session);
        await pulse.Invoked.Task;
        var reset = boundary.ResetAsync(_ => Task.CompletedTask);
        await WaitForCancellationAsync(lease.Token);
        pulse.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TimeSpan.FromSeconds(2)));
        await reset.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, pulse.StartCount);
        page.Deactivate();
    }

    private static async Task AssertRunningPulseIsCancelledPromptlyAsync(IServiceProvider services)
    {
        var boundary = services.GetRequiredService<IAccountSessionBoundary>();
        var generation = boundary.Capture();
        using var lease = boundary.CreateCancellationLease(generation);
        var session = new SetSavedFeedbackSession(
            lease.Token,
            phase => boundary.TryStartSessionPhase(generation, phase, lease.Token));
        var feedback = new MauiSetSavedFeedback(() => false);
        var pulse = new GatedPulseDriver(blockBeforeStart: false);
        var page = new TestSetLoggerPage(
            services.GetRequiredService<SetLoggerViewModel>(), feedback, pulse);
        page.Appear();

        var running = feedback.SetSavedAsync(new LocalSet(50m, null, 10), session);
        await pulse.Started.Task;
        var reset = boundary.ResetAsync(_ => Task.CompletedTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TimeSpan.FromSeconds(2)));
        await reset.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, pulse.StartCount);
        Assert.Equal(1, pulse.CancelCount);
        page.Deactivate();
    }

    private static Task WaitForCancellationAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(completion.SetResult);
        return completion.Task;
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

    private sealed class TestSetLoggerPage(
        SetLoggerViewModel viewModel,
        MauiSetSavedFeedback feedback,
        ISetSavedPulseDriver pulse) : SetLoggerPage(viewModel, feedback, pulse)
    {
        public void Appear() => base.OnAppearing();
    }

    private sealed class GatedPulseDriver(bool blockBeforeStart) : ISetSavedPulseDriver
    {
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartCount { get; private set; }
        public int CancelCount { get; private set; }

        public async Task InvokeAsync(Func<Task> action)
        {
            Invoked.TrySetResult();
            if (blockBeforeStart) await Release.Task;
            await action();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Cancel() => CancelCount++;
    }

    private sealed class HeadlessConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class HeadlessThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(
            string? thumbnailUri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class HeadlessPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
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
