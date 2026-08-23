using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Dispatching;
using System.Globalization;
using System.Text;
using TrackZ.Contracts.Exercises;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Contracts.Sync;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Components;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Train;
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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
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
            var application = app.Services.GetRequiredService<App>();
            _ = application.CreateTestWindow();
            await application.Initialization;

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
            ConfigureAuthenticatedServices(services);
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
            services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
            services.AddSingleton(new ProgressSnapshotCache(Path.Combine(root, "progress.json")));
            services.AddSingleton<IConnectivityService>(new HeadlessConnectivity());
            services.AddSingleton<IUiDispatcher>(new InlineUiDispatcher());
            services.AddSingleton<IExerciseThumbnailCache>(new HeadlessThumbnailCache());
            services.AddSingleton<IWorkoutPreferenceStore>(preferences);
            services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
            services.RemoveAll<IMobilePrivateDataCleaner>();
            services.AddSingleton<IMobilePrivateDataCleaner, MauiPrivateDataCleaner>();
            services.RemoveAll<MauiSetSavedFeedback>();
            services.AddSingleton(new MauiSetSavedFeedback(() => false));
            services.RemoveAll<ISetSavedFeedback>();
            services.AddSingleton<ISetSavedFeedback>(services =>
                services.GetRequiredService<MauiSetSavedFeedback>());
        }, new FixedLanguageStore(AppLanguage.English));

        try
        {
            var firstPage = app.Services.GetRequiredService<SetLoggerPage>();
            var secondPage = app.Services.GetRequiredService<SetLoggerPage>();
            var concreteStatus = app.Services.GetRequiredService<OutboxRepository>();
            var interfaceStatus = app.Services.GetRequiredService<IWorkoutOutboxStatusSource>();
            var firstHistoryPage = app.Services.GetRequiredService<WorkoutHistoryPage>();
            var secondHistoryPage = app.Services.GetRequiredService<WorkoutHistoryPage>();
            var firstGuidance = app.Services.GetRequiredService<IExerciseGuidancePreferenceStore>();
            var secondGuidance = app.Services.GetRequiredService<IExerciseGuidancePreferenceStore>();
            var guidanceExerciseId = Guid.NewGuid();

            Assert.IsType<SetLoggerViewModel>(firstPage.BindingContext);
            Assert.IsType<SetLoggerViewModel>(secondPage.BindingContext);
            Assert.Equal("Track sets", firstPage.Title);
            Assert.NotSame(firstPage, secondPage);
            Assert.NotSame(firstPage.BindingContext, secondPage.BindingContext);
            var firstLogger = Assert.IsType<SetLoggerViewModel>(firstPage.BindingContext);
            Assert.Same(
                firstLogger.BeginSetCommand,
                Assert.IsType<Button>(firstPage.FindByName("AddSetButton")).Command);
            Assert.Same(
                firstLogger.SaveDraftSetCommand,
                Assert.IsType<Button>(firstPage.FindByName("SaveDraftSetButton")).Command);
            Assert.False(Assert.IsType<Border>(firstPage.FindByName("InlineSetEditor")).IsVisible);
            Assert.False(
                Assert.IsType<SyncStatusPill>(firstPage.FindByName("SetLoggerSyncStatus")).IsVisible);
            Assert.Same(concreteStatus, interfaceStatus);
            Assert.Same(interfaceStatus, app.Services.GetRequiredService<IWorkoutOutboxStatusSource>());
            Assert.IsType<WorkoutHistoryViewModel>(firstHistoryPage.BindingContext);
            Assert.IsType<WorkoutHistoryViewModel>(secondHistoryPage.BindingContext);
            Assert.NotSame(firstHistoryPage, secondHistoryPage);
            Assert.NotSame(firstHistoryPage.BindingContext, secondHistoryPage.BindingContext);
            Assert.Same(concreteStatus, app.Services.GetRequiredService<IHistoryOutboxStatusSource>());
            Assert.Same(firstGuidance, secondGuidance);
            firstGuidance.SetIncrementKg(guidanceExerciseId, 2.5m);
            firstHistoryPage.Deactivate();
            secondHistoryPage.Deactivate();

            await AssertInlineSetEditorTransitionAsync(app.Services, firstLogger);

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

            await app.Services.GetRequiredService<IMobilePrivateDataCleaner>().ClearAsync();

            Assert.Null(firstGuidance.GetIncrementKg(guidanceExerciseId));
            Assert.Equal(
                WeightDisplayUnit.Pounds,
                app.Services.GetRequiredService<IWeightUnitPreference>().Current);
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
    public async Task Momentum_home_provider_keeps_one_page_and_view_model_while_loading_local_and_cached_state()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-momentum-composition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var originalDispatcherProvider = DispatcherProvider.Current;
        DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
        var boundary = new CountingSessionBoundary();
        var connectivity = new CountingConnectivity(isOnline: false);
        var units = new CountingWeightPreference();
        var exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var exerciseCache = new ExerciseCache(Path.Combine(root, "exercises.db"));
        var localDatabase = new TrackZLocalDatabase(Path.Combine(root, "workouts.db"));
        var localWorkouts = new LocalWorkoutRepository(localDatabase);
        var progressCache = new ProgressSnapshotCache(Path.Combine(root, "progress.json"));
        await exerciseCache.ReplaceAllAsync([
            new ExerciseSummaryDto(
                exerciseId,
                "Composed Pull",
                BodyPart.Back,
                TrackingMode.Weighted,
                null,
                DateTimeOffset.UtcNow,
                new PerformanceSetDto(70m, null, 8),
                new PerformanceSetDto(72.5m, null, 6),
                false)
        ], DateTimeOffset.UtcNow);
        var seededCoordinator = new ActiveWorkoutCoordinator(localWorkouts, boundary, new SystemClock());
        var seededWorkout = await seededCoordinator.StartAsync([
            new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
        ]);
        await seededCoordinator.SaveSetAsync(exerciseId, new LocalSet(70m, null, 8));
        var cachedProgress = CreateMomentumProgress();
        await progressCache.WriteAsync(cachedProgress);
        var app = MauiProgram.CreateMauiApp(services =>
        {
            ConfigureAuthenticatedServices(services);
            services.RemoveAll<IAccountSessionBoundary>();
            services.AddSingleton<IAccountSessionBoundary>(boundary);
            services.RemoveAll<IConnectivityService>();
            services.AddSingleton<IConnectivityService>(connectivity);
            services.RemoveAll<IWeightUnitPreference>();
            services.AddSingleton<IWeightUnitPreference>(units);
            services.RemoveAll<IProgressSnapshotSource>();
            services.RemoveAll<ProgressSnapshotSource>();
            services.RemoveAll<IProgressApi>();
            services.AddSingleton<IProgressApi, OfflineProgressApi>();
            services.RemoveAll<ProgressSnapshotCache>();
            services.AddSingleton(progressCache);
            services.AddSingleton<ProgressSnapshotSource>();
            services.AddSingleton<IProgressSnapshotSource>(provider =>
                provider.GetRequiredService<ProgressSnapshotSource>());
            services.RemoveAll<ITrainDashboardSource>();
            services.RemoveAll<LocalTrainDashboardSource>();
            services.AddSingleton<LocalTrainDashboardSource>();
            services.AddSingleton<ITrainDashboardSource>(provider =>
                provider.GetRequiredService<LocalTrainDashboardSource>());
            services.RemoveAll<ExerciseCache>();
            services.AddSingleton(exerciseCache);
            services.RemoveAll<TrackZLocalDatabase>();
            services.AddSingleton(localDatabase);
            services.RemoveAll<LocalWorkoutRepository>();
            services.AddSingleton(localWorkouts);
            services.RemoveAll<ILocalWorkoutRepository>();
            services.AddSingleton<ILocalWorkoutRepository>(provider =>
                provider.GetRequiredService<LocalWorkoutRepository>());
            services.RemoveAll<IWorkoutSyncTrigger>();
            services.AddSingleton<IWorkoutSyncTrigger, NoopWorkoutSyncTrigger>();
        });

        try
        {
            var firstPage = app.Services.GetRequiredService<TrainPage>();
            var secondPage = app.Services.GetRequiredService<TrainPage>();
            var viewModel = Assert.IsType<TrainTodayViewModel>(firstPage.BindingContext);
            var boundarySubscriptions = boundary.SessionResetSubscriptions;
            var connectivitySubscriptions = connectivity.Subscriptions;

            Assert.IsType<LocalTrainDashboardSource>(app.Services.GetRequiredService<ITrainDashboardSource>());
            Assert.IsType<ProgressSnapshotSource>(app.Services.GetRequiredService<IProgressSnapshotSource>());
            Assert.Same(localWorkouts, app.Services.GetRequiredService<ILocalWorkoutRepository>());
            Assert.Same(localWorkouts, app.Services.GetRequiredService<LocalWorkoutRepository>());
            Assert.Same(exerciseCache, app.Services.GetRequiredService<ExerciseCache>());
            Assert.Same(progressCache, app.Services.GetRequiredService<ProgressSnapshotCache>());
            Assert.IsType<MauiTrainNavigator>(app.Services.GetRequiredService<ITrainNavigator>());
            Assert.Equal(1, connectivitySubscriptions);

            InvokePageLifecycle(firstPage, "OnAppearing");
            await WaitUntilAsync(async () => (await localWorkouts.GetActiveAsync())?.Id == seededWorkout.Id
                && viewModel.ActiveWorkout?.WorkoutId == seededWorkout.Id
                && !viewModel.IsBusy);
            InvokePageLifecycle(firstPage, "OnDisappearing");
            var unitSubscriptions = units.Subscriptions;
            InvokePageLifecycle(firstPage, "OnAppearing");
            await WaitUntilAsync(() => Task.FromResult(
                viewModel.ActiveWorkout?.WorkoutId == seededWorkout.Id && !viewModel.IsBusy));
            InvokePageLifecycle(firstPage, "OnDisappearing");
            InvokePageLifecycle(firstPage, "OnAppearing");
            await WaitUntilAsync(() => Task.FromResult(
                viewModel.ActiveWorkout?.WorkoutId == seededWorkout.Id && !viewModel.IsBusy));
            InvokePageLifecycle(firstPage, "OnDisappearing");

            Assert.Same(firstPage, secondPage);
            Assert.Same(viewModel, secondPage.BindingContext);
            Assert.Same(viewModel, app.Services.GetRequiredService<TrainTodayViewModel>());
            Assert.Same(boundary, app.Services.GetRequiredService<IAccountSessionBoundary>());
            Assert.Same(connectivity, app.Services.GetRequiredService<IConnectivityService>());
            Assert.Same(units, app.Services.GetRequiredService<IWeightUnitPreference>());
            Assert.Equal(1, boundarySubscriptions);
            Assert.Equal(connectivitySubscriptions, connectivity.Subscriptions);
            Assert.Equal(1, unitSubscriptions);
            Assert.Equal(unitSubscriptions, units.Subscriptions);
            Assert.True(viewModel.ShowContinueHero);
            Assert.Equal(seededWorkout.Id, viewModel.ActiveWorkout!.WorkoutId);
            Assert.Equal([BodyPart.Back], viewModel.ActiveWorkout.BodyParts);
            Assert.Equal(1, viewModel.ActiveWorkout.ExerciseCount);
            Assert.Equal(1, viewModel.ActiveWorkout.LoggedExerciseCount);
            Assert.Equal(1, viewModel.ActiveWorkout.LoggedSetCount);
            Assert.True(viewModel.HasAuthoritativeProgress);
            Assert.Equal(8, viewModel.Level);
            Assert.Equal("Composed Press", viewModel.RecentMomentum!.ExerciseName);
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

    private static void InvokePageLifecycle(Page page, string methodName)
    {
        for (var type = page.GetType(); type is not null; type = type.BaseType)
        {
            var method = type.GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            if (method is null) continue;
            method.Invoke(page, null);
            return;
        }

        throw new MissingMethodException(page.GetType().FullName, methodName);
    }

    private static async Task AssertInlineSetEditorTransitionAsync(
        IServiceProvider services,
        SetLoggerViewModel logger)
    {
        var exerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await services.GetRequiredService<ActiveWorkoutCoordinator>().StartAsync([
            new WorkoutExerciseSelection(exerciseId, TrackingMode.Weighted)
        ]);
        await logger.LoadAsync(exerciseId, "Composed Press");
        var transition = new GatedInlineSetEditorTransition();
        var pulse = new CompletablePulseDriver();
        var page = new TestSetLoggerPage(
            logger,
            services.GetRequiredService<MauiSetSavedFeedback>(),
            pulse,
            transition);
        page.Appear();

        var scroll = page.FindByName<ScrollView>("SetLoggerScroll");
        var content = Assert.IsType<VerticalStackLayout>(scroll.Content);
        var editor = page.FindByName<Border>("InlineSetEditor");
        var exercise = page.FindByName<Grid>("ExerciseSummary");
        var today = page.FindByName<VerticalStackLayout>("TodaySetsSection");
        var previous = page.FindByName<VerticalStackLayout>("LastWorkoutSection");
        var noHistory = Assert.IsType<Label>(page.FindByName("NoSetHistoryLabel"));
        var reference = page.FindByName<Border>("PreviousWorkoutReferenceCard");
        var sync = Assert.IsType<SyncStatusPill>(page.FindByName("SetLoggerSyncStatus"));
        var addButton = page.FindByName<Button>("AddSetButton");
        var saveButton = page.FindByName<Button>("SaveDraftSetButton");
        var weightInput = page.FindByName<Entry>("DraftWeightInput");
        var repsInput = page.FindByName<Entry>("DraftRepsInput");
        Assert.True(content.Children.IndexOf(exercise) < content.Children.IndexOf(reference));
        Assert.True(content.Children.IndexOf(reference) < content.Children.IndexOf(editor));
        Assert.True(content.Children.IndexOf(editor) < content.Children.IndexOf(today));
        Assert.True(content.Children.IndexOf(today) < content.Children.IndexOf(previous));
        Assert.True(noHistory.IsVisible);
        Assert.Equal(logger.Text.NoPreviousSetsYet, noHistory.Text);
        Assert.False(today.IsVisible);
        Assert.False(previous.IsVisible);
        Assert.Equal(logger.HasPreviousReference, reference.IsVisible);
        Assert.False(sync.IsVisible);

        logger.BeginSetCommand.Execute(null);
        var cancelledReveal = await transition.NextAttemptAsync();

        Assert.True(editor.IsVisible);
        Assert.False(today.IsVisible);
        Assert.True(noHistory.IsVisible);
        Assert.False(addButton.IsVisible);
        Assert.True(saveButton.IsVisible);
        var saveSetOne = string.Format(CultureInfo.CurrentCulture, logger.Text.SaveSetNumberFormat, 1);
        Assert.Equal(saveSetOne, saveButton.Text);
        Assert.Equal(saveSetOne, SemanticProperties.GetDescription(saveButton));
        Assert.True(weightInput.IsVisible);
        Assert.True(repsInput.IsVisible);
        Assert.False(weightInput.IsFocused);
        Assert.False(repsInput.IsFocused);

        var incrementWeight = page.GetVisualTreeDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, logger.IncrementWeightCommand));
        incrementWeight.Command.Execute(incrementWeight.CommandParameter);

        Assert.Equal(0.5m, logger.DisplayWeight);
        Assert.Equal("0.5", logger.WeightInputText);
        Assert.Equal("0.5", weightInput.Text);

        Assert.Same(scroll, cancelledReveal.Scroll);
        Assert.Same(editor, cancelledReveal.Editor);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, logger.Text.NextSetFormat, 1),
            cancelledReveal.Announcement);

        logger.CancelDraftSetCommand.Execute(null);
        var cancelRestore = await transition.NextRestoredTargetAsync();
        await cancelledReveal.Exited.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Same(page.FindByName<Button>("AddSetButton"), cancelRestore);
        Assert.True(cancelledReveal.CancellationToken.IsCancellationRequested);
        Assert.False(cancelledReveal.Completed);

        logger.BeginSetCommand.Execute(null);
        var savedReveal = await transition.NextAttemptAsync();
        savedReveal.Release.TrySetResult();
        await savedReveal.Exited.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(savedReveal.Completed);
        logger.WeightKg = 70m;
        logger.Reps = 8;

        var saving = logger.SaveDraftSetCommand.ExecuteAsync();
        await pulse.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(logger.IsBusy);
        Assert.False(logger.CanBeginSet);
        Assert.Equal(1, transition.RestoreCount);

        pulse.Complete.TrySetResult();
        await saving.WaitAsync(TimeSpan.FromSeconds(1));
        var saveRestore = await transition.NextRestoredTargetAsync();

        Assert.False(logger.IsBusy);
        Assert.True(logger.CanBeginSet);
        Assert.Same(addButton, saveRestore);
        Assert.False(editor.IsVisible);
        Assert.True(today.IsVisible);
        Assert.False(noHistory.IsVisible);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, logger.Text.SaveSetNumberFormat, 2),
            saveButton.Text);

        logger.BeginSetCommand.Execute(null);
        var deactivatedReveal = await transition.NextAttemptAsync();
        page.Deactivate();
        await deactivatedReveal.Exited.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(deactivatedReveal.CancellationToken.IsCancellationRequested);
        Assert.False(deactivatedReveal.Completed);
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

    private static void ConfigureAuthenticatedServices(IServiceCollection services)
    {
        services.RemoveAll<IMobileTokenStorage>();
        services.AddSingleton<IMobileTokenStorage, AuthenticatedTokenStorage>();
        services.RemoveAll<IMobilePrivateDataCleaner>();
        services.AddSingleton<IMobilePrivateDataCleaner, NoopPrivateDataCleaner>();
        services.RemoveAll<ITrainDashboardSource>();
        services.AddSingleton<ITrainDashboardSource>(new FixedTrainDashboardSource(
            new TrainDashboardSnapshot(null, null)));
        services.RemoveAll<IProgressSnapshotSource>();
        services.AddSingleton<IProgressSnapshotSource>(new CachedProgressSource(CreateMomentumProgress()));
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

    private sealed class TestSetLoggerPage : SetLoggerPage
    {
        public TestSetLoggerPage(
            SetLoggerViewModel viewModel,
            MauiSetSavedFeedback feedback,
            ISetSavedPulseDriver pulse) : base(viewModel, feedback, pulse)
        {
        }

        public TestSetLoggerPage(
            SetLoggerViewModel viewModel,
            MauiSetSavedFeedback feedback,
            ISetSavedPulseDriver pulse,
            IInlineSetEditorTransition transition) : base(viewModel, feedback, pulse, transition)
        {
        }

        public void Appear() => base.OnAppearing();
    }

    private sealed class GatedInlineSetEditorTransition : IInlineSetEditorTransition
    {
        private readonly Queue<RevealAttempt> _attempts = new();
        private readonly SemaphoreSlim _attemptAvailable = new(0);
        private readonly Queue<VisualElement> _restoredTargets = new();
        private readonly SemaphoreSlim _restoreAvailable = new(0);
        public int RestoreCount { get; private set; }

        public async Task RevealAsync(
            ScrollView scroll,
            VisualElement editor,
            string announcement,
            CancellationToken cancellationToken)
        {
            var attempt = new RevealAttempt(
                scroll,
                editor,
                announcement,
                cancellationToken);
            lock (_attempts) _attempts.Enqueue(attempt);
            _attemptAvailable.Release();
            try
            {
                await attempt.Release.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                attempt.Completed = true;
            }
            finally
            {
                attempt.Exited.TrySetResult();
            }
        }

        public async Task<RevealAttempt> NextAttemptAsync()
        {
            await _attemptAvailable.WaitAsync(TimeSpan.FromSeconds(1));
            lock (_attempts) return _attempts.Dequeue();
        }

        public void RestoreFocus(VisualElement target)
        {
            lock (_restoredTargets)
            {
                _restoredTargets.Enqueue(target);
                RestoreCount++;
            }
            _restoreAvailable.Release();
        }

        public async Task<VisualElement> NextRestoredTargetAsync()
        {
            await _restoreAvailable.WaitAsync(TimeSpan.FromSeconds(1));
            lock (_restoredTargets) return _restoredTargets.Dequeue();
        }

        public sealed class RevealAttempt(
            ScrollView scroll,
            VisualElement editor,
            string announcement,
            CancellationToken cancellationToken)
        {
            public ScrollView Scroll { get; } = scroll;
            public VisualElement Editor { get; } = editor;
            public string Announcement { get; } = announcement;
            public CancellationToken CancellationToken { get; } = cancellationToken;
            public TaskCompletionSource Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Exited { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Completed { get; set; }
        }
    }

    private sealed class NoopPulseDriver : ISetSavedPulseDriver
    {
        public Task InvokeAsync(Func<Task> action) => action();
        public Task StartAsync(SetSavedOutcome outcome, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public void Cancel() { }
    }

    private sealed class CompletablePulseDriver : ISetSavedPulseDriver
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InvokeAsync(Func<Task> action) => action();

        public async Task StartAsync(
            SetSavedOutcome outcome,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Complete.Task.WaitAsync(cancellationToken);
        }

        public void Cancel() { }
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

        public async Task StartAsync(SetSavedOutcome outcome, CancellationToken cancellationToken)
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

    private static ProgressSnapshot CreateMomentumProgress() => new(
        new ProgressSummaryDto(
            1000m,
            500m,
            3,
            1,
            [new ExerciseProgressSummaryDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Composed Press",
                TrackingMode.Weighted,
                DateTimeOffset.UtcNow,
                70m,
                null,
                8,
                72.5m,
                null,
                6)]),
        new GamificationProfileDto(640, 8, 600, 800, 4, 3, 4, 4, [], []),
        DateTimeOffset.UtcNow);

    private sealed class FixedTrainDashboardSource(TrainDashboardSnapshot snapshot) : ITrainDashboardSource
    {
        public int LoadCount { get; private set; }

        public Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class CachedProgressSource(ProgressSnapshot cached) : IProgressSnapshotSource
    {
        public int CachedReadCount { get; private set; }

        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
        {
            CachedReadCount++;
            return Task.FromResult<ProgressSnapshot?>(cached);
        }

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("The composition host is offline."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(cached);
    }

    private sealed class OfflineProgressApi : IProgressApi
    {
        public Task<ProgressSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSummaryDto>(new HttpRequestException("The composition host is offline."));

        public Task<GamificationProfileDto> GetProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<GamificationProfileDto>(new HttpRequestException("The composition host is offline."));

        public Task<GamificationProfileDto> UpdatePreferencesAsync(
            int weeklyGoal,
            string timeZoneId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GamificationProfileDto>(new HttpRequestException("The composition host is offline."));
    }

    private sealed class NoopWorkoutSyncTrigger : IWorkoutSyncTrigger
    {
        public void NotifyMutation() { }
    }

    private sealed class CountingConnectivity(bool isOnline) : IConnectivityService
    {
        private EventHandler? _changed;
        public int Subscriptions { get; private set; }
        public bool IsOnline { get; } = isOnline;
        public event EventHandler? ConnectivityChanged
        {
            add { Subscriptions++; _changed += value; }
            remove { Subscriptions--; _changed -= value; }
        }
    }

    private sealed class CountingWeightPreference : IWeightUnitPreference
    {
        private EventHandler? _changed;
        public int Subscriptions { get; private set; }
        public WeightDisplayUnit Current { get; private set; } = WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed
        {
            add { Subscriptions++; _changed += value; }
            remove { Subscriptions--; _changed -= value; }
        }

        public void Set(WeightDisplayUnit unit)
        {
            if (Current == unit) return;
            Current = unit;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class CountingSessionBoundary : IAccountSessionBoundary
    {
        private readonly AccountSessionBoundary _inner = new();
        private EventHandler? _sessionReset;
        public int SessionResetSubscriptions { get; private set; }

        public event EventHandler? SessionReset
        {
            add { SessionResetSubscriptions++; _sessionReset += value; _inner.SessionReset += value; }
            remove { SessionResetSubscriptions--; _sessionReset -= value; _inner.SessionReset -= value; }
        }

        public AccountSessionGeneration Capture() => _inner.Capture();
        public bool IsCancellationRequested(AccountSessionGeneration generation) => _inner.IsCancellationRequested(generation);
        public AccountSessionCancellationLease CreateCancellationLease(
            AccountSessionGeneration generation,
            CancellationToken cancellationToken = default) => _inner.CreateCancellationLease(generation, cancellationToken);
        public bool TryStartSessionPhase(
            AccountSessionGeneration generation,
            Action phase,
            CancellationToken cancellationToken = default) => _inner.TryStartSessionPhase(generation, phase, cancellationToken);
        public Task<bool> TryCommitAsync(
            AccountSessionGeneration generation,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default) => _inner.TryCommitAsync(generation, mutation, cancellationToken);
        public Task ResetAsync(Func<CancellationToken, Task> reset, CancellationToken cancellationToken = default) =>
            _inner.ResetAsync(reset, cancellationToken);
        public Task<bool> TryResetAsync(
            AccountSessionGeneration generation,
            Func<CancellationToken, Task> reset,
            CancellationToken cancellationToken = default) => _inner.TryResetAsync(generation, reset, cancellationToken);
    }

    private sealed class AuthenticatedTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = new()
        {
            [MobileTokenKeys.AccessToken] = CreateAccessToken(),
            [MobileTokenKeys.RefreshToken] = "refresh-token",
            [MobileTokenKeys.UserId] = "99999999-9999-9999-9999-999999999999",
            [MobileTokenKeys.SessionId] = "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"
        };

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        private static string CreateAccessToken()
        {
            static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var expiration = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            return $"{Encode("{\"alg\":\"none\"}")}.{Encode($"{{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":{expiration}}}")}.signature";
        }
    }

    private sealed class NoopPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class FixedLanguageStore(AppLanguage language) : IAppLanguageStore
    {
        public AppLanguage Read() => language;
        public void Write(AppLanguage value) => language = value;
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
