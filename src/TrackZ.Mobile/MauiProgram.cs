using System.Globalization;
using Microsoft.Extensions.Logging;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Features.Profile;
using TrackZ.Mobile.Features.Summary;
using TrackZ.Mobile.Presentation;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Networking;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Localization;

namespace TrackZ.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp(
		Action<IServiceCollection>? configureTestServices = null,
		IAppLanguageStore? appLanguageStore = null)
	{
#if IOS || ANDROID
		appLanguageStore ??= new MauiAppLanguageStore(Preferences.Default);
#else
		appLanguageStore ??= new PortableAppLanguageStore();
#endif
		AppLanguageCulture.Apply(appLanguageStore.Read());
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>();
		builder.Services.AddSingleton<IAppLanguageStore>(appLanguageStore);
		builder.Services.AddSingleton<App>();
		builder.Services.AddSingleton<IApplication>(services => services.GetRequiredService<App>());
		builder.Services.AddSingleton<ILocalizedUiHost>(services => services.GetRequiredService<App>());
		builder.Services.AddSingleton<MauiAppLanguageChanger>();
		builder.Services.AddSingleton<IAppLanguageChanger>(services =>
			services.GetRequiredService<MauiAppLanguageChanger>());

		builder.Services.AddSingleton<IMobileTokenStorage, SecureMobileTokenStorage>();
		builder.Services.AddSingleton<IAccountSessionBoundary, AccountSessionBoundary>();
		builder.Services.AddSingleton<INativeSheetPresenter, MauiNativeSheetPresenter>();
		builder.Services.AddSingleton<MobileTokenStore>();
		builder.Services.AddSingleton<IMobilePrivateDataCleaner, MauiPrivateDataCleaner>();
		builder.Services.AddSingleton<IAccessTokenProvider>(services => services.GetRequiredService<MobileTokenStore>());
#if DEBUG
		var origins = MobileEndpointOrigins.Resolve(Environment.GetEnvironmentVariable);
#else
		var origins = MobileEndpointOrigins.Resolve(_ => null);
#endif
		var apiOrigin = origins.ApiOrigin;
		var mediaOrigin = origins.MediaOrigin;
		builder.Services.AddSingleton(new IdentityHttpTransport(new HttpClient(new HttpClientHandler
		{
			AllowAutoRedirect = false
		})
		{
			BaseAddress = apiOrigin,
			Timeout = TimeSpan.FromSeconds(30)
		}));
		builder.Services.AddSingleton(services => new TrackZIdentityRefreshClient(
			services.GetRequiredService<IdentityHttpTransport>().HttpClient,
			services.GetRequiredService<MobileTokenStore>(),
			services.GetRequiredService<IAccountSessionBoundary>()));
		builder.Services.AddSingleton(services => new TrackZIdentityApiClient(
			services.GetRequiredService<IdentityHttpTransport>().HttpClient,
			services.GetRequiredService<MobileTokenStore>(),
			services.GetRequiredService<IMobilePrivateDataCleaner>(),
			services.GetRequiredService<IAccountSessionBoundary>(),
			services.GetRequiredService<TrackZIdentityRefreshClient>(),
			apiOrigin));
		builder.Services.AddSingleton<IIdentitySessionApi>(services => services.GetRequiredService<TrackZIdentityApiClient>());
		builder.Services.AddSingleton<IDeviceNameProvider, MauiDeviceNameProvider>();
		builder.Services.AddScoped(_ => AuthTextSet.For(CultureInfo.CurrentUICulture));
		builder.Services.AddSingleton<AuthGateCoordinator>();
		builder.Services.AddSingleton<IAuthEntryPoint, MauiAuthEntryPoint>();
		builder.Services.AddSingleton<IProtectedRequestAuthentication, ProtectedRequestAuthentication>();
		builder.Services.AddSingleton(services => new HttpClient(
			new AuthenticatedApiHandler(
				services.GetRequiredService<IAccessTokenProvider>(),
				services.GetRequiredService<IProtectedRequestAuthentication>(),
				apiOrigin,
				services.GetRequiredService<IAccountSessionBoundary>())
			{
				InnerHandler = new HttpClientHandler { AllowAutoRedirect = false }
			})
		{
			BaseAddress = apiOrigin,
			Timeout = TimeSpan.FromSeconds(30)
		});
		builder.Services.AddSingleton(new SignedMediaDownloadClient(new HttpClient(new HttpClientHandler
		{
			AllowAutoRedirect = false
		})
		{
			Timeout = TimeSpan.FromSeconds(30)
		}));
		builder.Services.AddSingleton<TrackZExerciseApiClient>();
		builder.Services.AddSingleton<TrackZSyncApiClient>();
		builder.Services.AddSingleton<TrackZProgressApiClient>();
		builder.Services.AddSingleton<IProgressApi>(services => services.GetRequiredService<TrackZProgressApiClient>());
		builder.Services.AddSingleton<ISyncApi>(services => services.GetRequiredService<TrackZSyncApiClient>());
		builder.Services.AddSingleton<IExerciseCatalogApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<IExerciseHistoryApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<ICustomExerciseApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<IExerciseImageApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<IExerciseThumbnailCache>(services => new AuthenticatedExerciseThumbnailCache(
			services.GetRequiredService<HttpClient>(),
			services.GetRequiredService<SignedMediaDownloadClient>().HttpClient,
			mediaOrigin,
			Path.Combine(FileSystem.AppDataDirectory, "exercise-thumbnails"),
			services.GetRequiredService<IClock>(),
			services.GetRequiredService<IAccountSessionBoundary>()));
		builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton(TimeZoneInfo.Local);
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<IRetryDelay, SystemRetryDelay>();
		builder.Services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
		builder.Services.AddSingleton<ILocalExerciseImagePicker, MauiLocalExerciseImagePicker>();
		builder.Services.AddSingleton<IExerciseFileStore, LocalExerciseFileStore>();
		builder.Services.AddSingleton(services => new ExerciseCache(
			Path.Combine(FileSystem.AppDataDirectory, "exercise-catalog.db")));
		builder.Services.AddSingleton(services => new ExerciseHistoryCache(
			Path.Combine(FileSystem.AppDataDirectory, "exercise-history.db")));
		builder.Services.AddSingleton(services => new TrackZLocalDatabase(
			Path.Combine(FileSystem.AppDataDirectory, "workouts.db")));
		builder.Services.AddSingleton(services => new ProgressSnapshotCache(
			Path.Combine(FileSystem.AppDataDirectory, "progress-snapshot.json")));
		builder.Services.AddSingleton<ProgressSnapshotSource>();
		builder.Services.AddSingleton<IProgressSnapshotSource>(services => services.GetRequiredService<ProgressSnapshotSource>());
		builder.Services.AddSingleton<LocalWorkoutRepository>();
		builder.Services.AddSingleton<ILocalWorkoutRepository>(services =>
			services.GetRequiredService<LocalWorkoutRepository>());
		builder.Services.AddSingleton<ICompletedWorkoutSummarySource, CompletedWorkoutSummarySource>();
		builder.Services.AddSingleton<ITrainDashboardSource, LocalTrainDashboardSource>();
		builder.Services.AddSingleton<OutboxRepository>();
		builder.Services.AddSingleton<IWorkoutOutboxStatusSource>(services =>
			services.GetRequiredService<OutboxRepository>());
		builder.Services.AddSingleton<IHistoryOutboxStatusSource>(services =>
			services.GetRequiredService<OutboxRepository>());
		builder.Services.AddSingleton<ActiveWorkoutCoordinator>();
		builder.Services.AddSingleton<ISetEffortRecorder>(services =>
			services.GetRequiredService<ActiveWorkoutCoordinator>());
		builder.Services.AddSingleton<WorkoutHistoryCoordinator>();
		builder.Services.AddSingleton<IExerciseHistorySource, CachedExerciseHistorySource>();
		builder.Services.AddSingleton<IWorkoutSyncRunner, WorkoutSyncRunner>();
		builder.Services.AddScoped(_ => WorkoutResources.ForCulture(CultureInfo.CurrentUICulture));
		builder.Services.AddScoped(_ => GamificationResources.Current);
		builder.Services.AddScoped(_ => MobileResources.ForCulture(CultureInfo.CurrentUICulture));
		builder.Services.AddSingleton<LocalizedUiScopeManager>();
		builder.Services.AddSingleton<IWorkoutPreferenceStore, MauiWorkoutPreferenceStore>();
		builder.Services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
		builder.Services.AddSingleton<IExerciseGuidancePreferenceStore, ExerciseGuidancePreferenceStore>();
		builder.Services.AddScoped<MauiSetSavedFeedback>();
		builder.Services.AddScoped<ISetSavedFeedback>(services => services.GetRequiredService<MauiSetSavedFeedback>());
		builder.Services.AddSingleton<IReduceMotionPreference, MauiReduceMotionPreference>();
		builder.Services.AddSingleton<ITrackZMotion, MauiTrackZMotion>();
		builder.Services.AddSingleton<SyncCoordinator>();
		builder.Services.AddSingleton<ISyncAuthenticationRecovery, MauiSyncAuthenticationRecovery>();
		builder.Services.AddSingleton<WorkoutSyncOrchestrator>();
		builder.Services.AddSingleton<IWorkoutSyncTrigger>(services =>
			services.GetRequiredService<WorkoutSyncOrchestrator>());
		builder.Services.AddSingleton<IWorkoutSyncLifecycle>(services =>
			services.GetRequiredService<WorkoutSyncOrchestrator>());
		builder.Services.AddSingleton<ConflictResolution>();
		builder.Services.AddSingleton<IConflictResolution>(services =>
			services.GetRequiredService<ConflictResolution>());
		builder.Services.AddSingleton<IHistoryConfirmation, MauiHistoryConfirmation>();
		builder.Services.AddSingleton<CustomExerciseImageService>();
		builder.Services.AddSingleton<LocalExerciseImageImporter>();
		builder.Services.AddSingleton<LocalExerciseImageSelectionCoordinator>();
		builder.Services.AddScoped<ExercisePickerViewModel>();
		builder.Services.AddTransient<CustomExerciseViewModel>();
		builder.Services.AddScoped<WorkoutViewModel>();
		builder.Services.AddTransient<SetLoggerViewModel>();
		builder.Services.AddTransient<SetEffortPromptViewModel>();
		builder.Services.AddSingleton<IInlineSetEditorTransition, MauiInlineSetEditorTransition>();
		builder.Services.AddTransient<WorkoutHistoryViewModel>();
		builder.Services.AddTransient<WorkoutHistoryDetailViewModel>();
		builder.Services.AddTransient<WorkoutSummaryViewModel>();
		builder.Services.AddTransient<ExerciseProgressViewModel>();
		builder.Services.AddScoped<ProgressDashboardViewModel>();
		builder.Services.AddScoped<ProfileViewModel>();
		builder.Services.AddScoped(services => new TrainTodayViewModel(
			services.GetRequiredService<ITrainDashboardSource>(),
			services.GetRequiredService<IAccountSessionBoundary>(),
			services.GetRequiredService<WorkoutTextSet>(),
			services.GetRequiredService<IProgressSnapshotSource>(),
			services.GetRequiredService<IConnectivityService>(),
			services.GetRequiredService<IWeightUnitPreference>(),
			services.GetRequiredService<GamificationTextSet>(),
			services.GetRequiredService<ActiveWorkoutCoordinator>(),
			services.GetRequiredService<ITrainNavigator>(),
			services.GetRequiredService<IClock>(),
			services.GetRequiredService<TimeZoneInfo>()));
		builder.Services.AddTransient<BodyAreaSheetPage>();
		builder.Services.AddSingleton<IBodyAreaPicker>(services => new MauiBodyAreaPicker(
			services.GetRequiredService<INativeSheetPresenter>(),
			services.GetRequiredService<BodyAreaSheetPage>));
		builder.Services.AddSingleton<ITrainNavigator, MauiTrainNavigator>();
		builder.Services.AddSingleton<IExercisePickerNavigator, MauiExercisePickerNavigator>();
		builder.Services.AddSingleton<IExercisePickerWarning, MauiExercisePickerWarning>();
		builder.Services.AddScoped<ExercisePickerPage>();
		builder.Services.AddTransient<CustomExercisePage>();
		builder.Services.AddScoped<WorkoutPage>();
		builder.Services.AddTransient<SetLoggerPage>();
		builder.Services.AddTransient<SetEffortSheetPage>();
		builder.Services.AddTransient<ISetEffortSheet>(services =>
			services.GetRequiredService<SetEffortSheetPage>());
		builder.Services.AddTransient<SetEntrySheetPage>();
		builder.Services.AddTransient<WorkoutHistoryPage>();
		builder.Services.AddTransient<WorkoutHistoryDetailPage>();
		builder.Services.AddTransient<HistorySetEditorSheetPage>();
		builder.Services.AddTransient<HistoryConflictSheetPage>();
		builder.Services.AddTransient<WorkoutSummaryPage>();
		builder.Services.AddScoped<ExerciseProgressPage>();
		builder.Services.AddScoped<ProfilePage>();
		builder.Services.AddScoped<TrainPage>();
		builder.Services.AddTransient<AuthFormViewModel>();
		builder.Services.AddTransient<SignInPage>();
		builder.Services.AddTransient<CreateAccountPage>();
		builder.Services.AddScoped<AuthGatePage>();
		builder.Services.AddScoped<AuthShell>();
		builder.Services.AddScoped<AppShell>();
		configureTestServices?.Invoke(builder.Services);

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}

internal sealed class PortableAppLanguageStore : IAppLanguageStore
{
	private AppLanguage _language = AppLanguage.Thai;

	public AppLanguage Read() => _language;
	public void Write(AppLanguage language) => _language = language;
}

public sealed class MauiAuthEntryPoint(IServiceProvider services) : IAuthEntryPoint
{
	public Task RequireSignInAsync(CancellationToken cancellationToken = default) =>
		services.GetRequiredService<AuthGateCoordinator>().RequireSignInAsync(cancellationToken);

	public Task RequireSignInAsync(
		AccountSessionGeneration expectedGeneration,
		CancellationToken cancellationToken = default) =>
		services.GetRequiredService<AuthGateCoordinator>().RequireSignInAsync(
			expectedGeneration, cancellationToken);
}
