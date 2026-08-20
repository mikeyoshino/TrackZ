using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using System.Reflection;
using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Sync;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Architecture;

public sealed class MauiCompositionTests
{
    [Fact]
    public async Task Relaunch_does_not_push_workout_while_custom_creation_is_unresolved()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-custom-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var exercisePath = Path.Combine(root, "exercises.db");
        var workoutPath = Path.Combine(root, "workouts.db");
        var exerciseId = Guid.NewGuid();
        await new ExerciseCache(exercisePath).QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), exerciseId, null, "Offline Network Press", BodyPart.Chest,
            TrackingMode.Weighted, null, null, null, DateTimeOffset.UtcNow));
        await new ActiveWorkoutCoordinator(
            new LocalWorkoutRepository(new TrackZLocalDatabase(workoutPath)),
            new AccountSessionBoundary(),
            new SystemClock()).StartAsync([
                new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
            ]);

        var events = new List<string>();
        var customApi = new LifecycleCustomApi(events, failCreate: true);
        var workoutApi = new LifecycleSyncApi(events, customApi);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(exercisePath));
            services.AddSingleton(new TrackZLocalDatabase(workoutPath));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ICustomExerciseApi>(customApi);
            services.AddSingleton<ISyncApi>(workoutApi);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await customApi.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var first = await Task.WhenAny(
                workoutApi.PushAttempted.Task,
                Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.NotSame(workoutApi.PushAttempted.Task, first);
            Assert.Empty(workoutApi.Pushes);
            Assert.Single(await app.Services.GetRequiredService<ExerciseCache>().GetPendingAsync());
            Assert.Single(await app.Services.GetRequiredService<OutboxRepository>().PendingAsync());
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Relaunch_retries_transient_custom_create_without_an_external_signal_before_pushing_workout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-custom-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var exercisePath = Path.Combine(root, "exercises.db");
        var workoutPath = Path.Combine(root, "workouts.db");
        var exerciseId = Guid.NewGuid();
        await new ExerciseCache(exercisePath).QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), exerciseId, null, "Retry Stable Press", BodyPart.Chest,
            TrackingMode.Weighted, null, null, null, DateTimeOffset.UtcNow));
        await new ActiveWorkoutCoordinator(
            new LocalWorkoutRepository(new TrackZLocalDatabase(workoutPath)),
            new AccountSessionBoundary(),
            new SystemClock()).StartAsync([
                new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
            ]);

        var events = new List<string>();
        var customApi = new LifecycleCustomApi(events, transientCreateFailures: 1);
        var workoutApi = new LifecycleSyncApi(events, customApi);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(exercisePath));
            services.AddSingleton(new TrackZLocalDatabase(workoutPath));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ICustomExerciseApi>(customApi);
            services.AddSingleton<ISyncApi>(workoutApi);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<ExerciseCache>().GetPendingAsync()).Count == 0
                && (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Equal(2, customApi.CreateAttempts);
            Assert.Equal(exerciseId, Assert.Single(customApi.Created));
            Assert.Equal(
                ["custom-create", "custom-create", "custom-update", "workout-StartWorkout"],
                events);
            var start = Assert.Single(Assert.Single(workoutApi.Pushes).Operations);
            Assert.Equal(exerciseId,
                start.Payload.GetProperty("exercises")[0].GetProperty("exerciseDefinitionId").GetGuid());
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Relaunch_refreshes_auth_once_before_retrying_custom_then_dependent_workout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-custom-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var exercisePath = Path.Combine(root, "exercises.db");
        var workoutPath = Path.Combine(root, "workouts.db");
        var exerciseId = Guid.NewGuid();
        await new ExerciseCache(exercisePath).QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), exerciseId, null, "Auth Stable Press", BodyPart.Chest,
            TrackingMode.Weighted, null, null, null, DateTimeOffset.UtcNow));
        await new ActiveWorkoutCoordinator(
            new LocalWorkoutRepository(new TrackZLocalDatabase(workoutPath)),
            new AccountSessionBoundary(),
            new SystemClock()).StartAsync([
                new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
            ]);

        var events = new List<string>();
        var customApi = new LifecycleCustomApi(events, authenticationFailures: 1);
        var workoutApi = new LifecycleSyncApi(events, customApi);
        var recovery = new LifecycleAuthenticationRecovery();
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(exercisePath));
            services.AddSingleton(new TrackZLocalDatabase(workoutPath));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ICustomExerciseApi>(customApi);
            services.AddSingleton<ISyncApi>(workoutApi);
            services.AddSingleton<ISyncAuthenticationRecovery>(recovery);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<ExerciseCache>().GetPendingAsync()).Count == 0
                && (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Equal(1, recovery.Attempts);
            Assert.Equal(2, customApi.CreateAttempts);
            Assert.Equal(exerciseId, Assert.Single(customApi.Created));
            Assert.Equal(
                ["custom-create", "custom-create", "custom-update", "workout-StartWorkout"],
                events);
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Relaunch_syncs_pending_custom_before_workout_that_uses_its_stable_id()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-custom-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var exercisePath = Path.Combine(root, "exercises.db");
        var workoutPath = Path.Combine(root, "workouts.db");
        var exerciseId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 8, 16, 7, 0, 0, TimeSpan.Zero);
        await new ExerciseCache(exercisePath).QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), exerciseId, null, "Offline Stable Press", BodyPart.Chest,
            TrackingMode.Weighted, null, null, null, createdAt));
        await new ActiveWorkoutCoordinator(
            new LocalWorkoutRepository(new TrackZLocalDatabase(workoutPath)),
            new AccountSessionBoundary(),
            new SystemClock()).StartAsync([
                new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
            ]);

        // Simulate a hard kill: only the two SQLite files cross this process boundary.
        var events = new List<string>();
        var customApi = new LifecycleCustomApi(events);
        var workoutApi = new LifecycleSyncApi(events, customApi);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(exercisePath));
            services.AddSingleton(new TrackZLocalDatabase(workoutPath));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ICustomExerciseApi>(customApi);
            services.AddSingleton<ISyncApi>(workoutApi);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<ExerciseCache>().GetPendingAsync()).Count == 0
                && (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Equal(["custom-create", "custom-update", "workout-StartWorkout"], events);
            Assert.Equal(exerciseId, Assert.Single(customApi.Created));
            var start = Assert.Single(Assert.Single(workoutApi.Pushes).Operations);
            Assert.Equal(exerciseId,
                start.Payload.GetProperty("exercises")[0].GetProperty("exerciseDefinitionId").GetGuid());
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Real_app_window_lifecycle_uploads_durable_workout_without_manual_coordinator_run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-lifecycle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var api = new LifecycleSyncApi();
        var connectivity = new LifecycleConnectivity();
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton<IConnectivityService>(connectivity);
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ISyncApi>(api);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            await app.Services.GetRequiredService<ActiveWorkoutCoordinator>().StartAsync([
                new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)
            ]);
            Assert.Single(await app.Services.GetRequiredService<OutboxRepository>().PendingAsync());

            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Single(api.Pushes);
            Assert.Equal("StartWorkout", Assert.Single(api.Pushes[0].Operations).Action);
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Due_retry_waits_for_reconnect_while_offline_instead_of_spinning()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-lifecycle-due-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var api = new LifecycleSyncApi(retryablePushes: 1);
        var connectivity = new LifecycleConnectivity();
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton<IConnectivityService>(connectivity);
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ISyncApi>(api);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            await app.Services.GetRequiredService<ActiveWorkoutCoordinator>().StartAsync([
                new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)
            ]);
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await api.PushAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            connectivity.Disconnect();
            await Task.Delay(TimeSpan.FromMilliseconds(1250));
            var readsBefore = connectivity.IsOnlineReadCount;
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            Assert.InRange(connectivity.IsOnlineReadCount - readsBefore, 0, 1);
            Assert.Single(api.Pushes);
            var pending = Assert.Single(
                await app.Services.GetRequiredService<OutboxRepository>().PendingAsync());
            Assert.Equal(1, pending.RetryCount);

            connectivity.Reconnect();
            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);
            Assert.Equal(2, api.Pushes.Count);
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Authentication_failure_refreshes_once_and_retries_without_erasing_pending_workout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-lifecycle-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var api = new LifecycleSyncApi(authenticationFailures: 1);
        var recovery = new LifecycleAuthenticationRecovery();
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ISyncApi>(api);
            services.AddSingleton<ISyncAuthenticationRecovery>(recovery);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            await app.Services.GetRequiredService<ActiveWorkoutCoordinator>().StartAsync([
                new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)
            ]);
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Equal(1, recovery.Attempts);
            Assert.Equal(2, api.Pushes.Count);
            Assert.NotNull(await app.Services.GetRequiredService<LocalWorkoutRepository>()
                .GetActiveAsync());
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task One_lifecycle_wake_spends_only_one_auth_recovery_across_custom_and_workout_sync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-lifecycle-auth-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var exercisePath = Path.Combine(root, "exercises.db");
        var workoutPath = Path.Combine(root, "workouts.db");
        var exerciseId = Guid.NewGuid();
        await new ExerciseCache(exercisePath).QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), exerciseId, null, "Auth Budget Press", BodyPart.Chest,
            TrackingMode.Weighted, null, null, null, DateTimeOffset.UtcNow));
        var events = new List<string>();
        var customApi = new LifecycleCustomApi(events, authenticationFailures: 1);
        var workoutApi = new LifecycleSyncApi(events, customApi, authenticationFailures: 1);
        var recovery = new LifecycleAuthenticationRecovery();
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(exercisePath));
            services.AddSingleton(new TrackZLocalDatabase(workoutPath));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ICustomExerciseApi>(customApi);
            services.AddSingleton<ISyncApi>(workoutApi);
            services.AddSingleton<ISyncAuthenticationRecovery>(recovery);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            await app.Services.GetRequiredService<ActiveWorkoutCoordinator>().StartAsync([
                new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
            ]);
            var lifecycle = app.Services.GetRequiredService<IWorkoutSyncLifecycle>();
            var application = new App(
                app.Services.GetRequiredService<AppShell>(), lifecycle);
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await workoutApi.PushAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(100);

            Assert.Equal(1, recovery.Attempts);
            Assert.Single(workoutApi.Pushes);
            Assert.Single(await app.Services.GetRequiredService<OutboxRepository>().PendingAsync());
            Assert.Empty(await app.Services.GetRequiredService<ExerciseCache>().GetPendingAsync());

            lifecycle.Resume();
            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Equal(1, recovery.Attempts);
            Assert.Equal(2, workoutApi.Pushes.Count);
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Permanent_predecessor_schedules_its_causal_successor_without_an_external_signal()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-lifecycle-permanent-successor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var api = new LifecycleSyncApi(permanentFailures: 1);
        var app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton<IConnectivityService>(new LifecycleConnectivity());
            services.AddSingleton<IAccessTokenProvider>(new LifecycleTokenProvider());
            services.AddSingleton<ISyncApi>(api);
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(new HeadlessPreferences());
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
        });

        try
        {
            var active = app.Services.GetRequiredService<ActiveWorkoutCoordinator>();
            var local = await active.StartAsync([
                new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)
            ]);
            await active.SaveSetAsync(
                local.Exercises[0].ExerciseDefinitionId,
                new LocalSet(70m, null, 8));
            var application = new App(
                app.Services.GetRequiredService<AppShell>(),
                app.Services.GetRequiredService<IWorkoutSyncLifecycle>());
            var createWindow = typeof(App).GetMethod(
                "CreateWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            _ = Assert.IsType<Window>(createWindow.Invoke(application, [null]));

            await WaitUntilAsync(async () =>
                (await app.Services.GetRequiredService<OutboxRepository>().PendingAsync()).Count == 0);

            Assert.Equal(2, api.Pushes.Count);
            var rejected = Assert.Single(
                await app.Services.GetRequiredService<OutboxRepository>().RejectedAsync(local.Id));
            Assert.Equal(OutboxOperationType.StartWorkout, rejected.Type);
            Assert.Equal(TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest, rejected.FailureCode);
        }
        finally
        {
            app.Dispose();
            DispatcherProvider.SetCurrent(originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

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
            var firstHistoryPage = app.Services.GetRequiredService<WorkoutHistoryPage>();
            var secondHistoryPage = app.Services.GetRequiredService<WorkoutHistoryPage>();

            Assert.IsType<SetLoggerViewModel>(firstPage.BindingContext);
            Assert.IsType<SetLoggerViewModel>(secondPage.BindingContext);
            Assert.NotSame(firstPage, secondPage);
            Assert.NotSame(firstPage.BindingContext, secondPage.BindingContext);
            Assert.Same(concreteStatus, interfaceStatus);
            Assert.Same(interfaceStatus, app.Services.GetRequiredService<IWorkoutOutboxStatusSource>());
            Assert.IsType<WorkoutHistoryViewModel>(firstHistoryPage.BindingContext);
            Assert.IsType<WorkoutHistoryViewModel>(secondHistoryPage.BindingContext);
            Assert.NotSame(firstHistoryPage, secondHistoryPage);
            Assert.NotSame(firstHistoryPage.BindingContext, secondHistoryPage.BindingContext);
            Assert.Same(concreteStatus, app.Services.GetRequiredService<IHistoryOutboxStatusSource>());
            firstHistoryPage.Deactivate();
            secondHistoryPage.Deactivate();

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

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!await predicate()) await Task.Delay(20, timeout.Token);
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

    private sealed class LifecycleConnectivity : IConnectivityService
    {
        private int _isOnline = 1;
        private int _isOnlineReadCount;
        public bool IsOnline
        {
            get
            {
                Interlocked.Increment(ref _isOnlineReadCount);
                return Volatile.Read(ref _isOnline) == 1;
            }
        }
        public int IsOnlineReadCount => Volatile.Read(ref _isOnlineReadCount);
        public event EventHandler? ConnectivityChanged;
        public void Disconnect() => Volatile.Write(ref _isOnline, 0);
        public void Reconnect()
        {
            Volatile.Write(ref _isOnline, 1);
            ConnectivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class LifecycleTokenProvider : IAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("authenticated-test-token");
    }

    private sealed class LifecycleSyncApi(
        List<string>? events = null,
        LifecycleCustomApi? customApi = null,
        int retryablePushes = 0,
        int authenticationFailures = 0,
        int permanentFailures = 0) : ISyncApi
    {
        private int _pushAttempts;
        public List<SyncPushRequest> Pushes { get; } = [];
        public TaskCompletionSource PushAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SyncPushResponse> PushAsync(
            SyncPushRequest request,
            CancellationToken cancellationToken = default)
        {
            Pushes.Add(request);
            PushAttempted.TrySetResult();
            var attempt = Interlocked.Increment(ref _pushAttempts);
            if (attempt <= permanentFailures)
                throw new SyncApiException(
                    SyncApiFailureKind.Permanent,
                    System.Net.HttpStatusCode.BadRequest,
                    TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest,
                    "trace-permanent",
                    "invalid operation");
            if (attempt <= authenticationFailures)
                throw new SyncApiException(
                    SyncApiFailureKind.Authentication,
                    System.Net.HttpStatusCode.Unauthorized,
                    TrackZ.Contracts.Errors.BusinessErrorCode.InvalidCredentials,
                    "trace-auth",
                    "expired access token");
            foreach (var operation in request.Operations)
            {
                if (customApi is not null)
                {
                    var exerciseId = operation.Payload.GetProperty("exercises")[0]
                        .GetProperty("exerciseDefinitionId").GetGuid();
                    Assert.Contains(exerciseId, customApi.Created);
                }
                events?.Add($"workout-{operation.Action}");
            }
            return Task.FromResult(new SyncPushResponse(request.Operations.Select(operation =>
                attempt <= retryablePushes
                    ? new SyncOperationResultDto(
                        operation.OperationId,
                        SyncOperationStatus.Retryable,
                        null,
                        TrackZ.Contracts.Errors.BusinessErrorCode.InternalServerError)
                    : new SyncOperationResultDto(
                        operation.OperationId,
                        SyncOperationStatus.Applied,
                        Math.Max(1, operation.BaseVersion.GetValueOrDefault() + 1),
                        null)).ToArray()));
        }

        public Task<SyncPullResponse> PullAsync(
            string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SyncPullResponse([], cursor, false));
    }

    private sealed class LifecycleAuthenticationRecovery : ISyncAuthenticationRecovery
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        public Task<bool> TryRecoverAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(true);
        }
    }

    private sealed class LifecycleCustomApi(
        List<string> events,
        bool failCreate = false,
        int transientCreateFailures = 0,
        int authenticationFailures = 0) : ICustomExerciseApi
    {
        private int _createAttempts;

        public List<Guid> Created { get; } = [];
        public int CreateAttempts => Volatile.Read(ref _createAttempts);
        public TaskCompletionSource CreateEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Guid> CreateAsync(
            CustomExerciseDraft exercise,
            CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _createAttempts);
            events.Add("custom-create");
            CreateEntered.TrySetResult();
            if (attempt <= authenticationFailures)
                throw new MobileApiException(
                    TrackZ.Contracts.Errors.BusinessErrorCode.InvalidCredentials,
                    "expired access token",
                    isAuthenticationRequired: true);
            if (failCreate || attempt <= transientCreateFailures)
                throw new HttpRequestException("Network unavailable.");
            Created.Add(exercise.LocalExerciseId);
            return Task.FromResult(exercise.LocalExerciseId);
        }

        public Task UpdateAsync(
            Guid exerciseId,
            CustomExerciseDraft exercise,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(exercise.LocalExerciseId, exerciseId);
            events.Add("custom-update");
            return Task.CompletedTask;
        }
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
