using Microsoft.Extensions.Logging;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp(Action<IServiceCollection>? configureTestServices = null)
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<IMobileTokenStorage, SecureMobileTokenStorage>();
		builder.Services.AddSingleton<IAccountSessionBoundary, AccountSessionBoundary>();
		builder.Services.AddSingleton<MobileTokenStore>();
		builder.Services.AddSingleton<IMobilePrivateDataCleaner, MauiPrivateDataCleaner>();
		builder.Services.AddSingleton<IAccessTokenProvider>(services => services.GetRequiredService<MobileTokenStore>());
		var apiOrigin = new Uri("https://api.trackz.app");
		var mediaOrigin = new Uri("https://media.trackz.app");
		builder.Services.AddSingleton(services => new HttpClient(
			new BearerTokenHandler(services.GetRequiredService<IAccessTokenProvider>(), apiOrigin)
			{
				InnerHandler = new HttpClientHandler()
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
		builder.Services.AddSingleton<TrackZIdentityRefreshClient>();
		builder.Services.AddSingleton<TrackZIdentityApiClient>();
		builder.Services.AddSingleton<TrackZSyncApiClient>();
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
		builder.Services.AddSingleton<LocalWorkoutRepository>();
		builder.Services.AddSingleton<ILocalWorkoutRepository>(services =>
			services.GetRequiredService<LocalWorkoutRepository>());
		builder.Services.AddSingleton<OutboxRepository>();
		builder.Services.AddSingleton<IWorkoutOutboxStatusSource>(services =>
			services.GetRequiredService<OutboxRepository>());
		builder.Services.AddSingleton<IHistoryOutboxStatusSource>(services =>
			services.GetRequiredService<OutboxRepository>());
		builder.Services.AddSingleton<ActiveWorkoutCoordinator>();
		builder.Services.AddSingleton<WorkoutHistoryCoordinator>();
		builder.Services.AddSingleton<IExerciseHistorySource, CachedExerciseHistorySource>();
		builder.Services.AddSingleton<IWorkoutSyncRunner, WorkoutSyncRunner>();
		builder.Services.AddSingleton(WorkoutResources.Current);
		builder.Services.AddSingleton<IWorkoutPreferenceStore, MauiWorkoutPreferenceStore>();
		builder.Services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
		builder.Services.AddSingleton<MauiSetSavedFeedback>();
		builder.Services.AddSingleton<ISetSavedFeedback>(services => services.GetRequiredService<MauiSetSavedFeedback>());
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
		builder.Services.AddTransient<ExercisePickerViewModel>();
		builder.Services.AddTransient<CustomExerciseViewModel>();
		builder.Services.AddTransient<WorkoutViewModel>();
		builder.Services.AddTransient<SetLoggerViewModel>();
		builder.Services.AddTransient<WorkoutHistoryViewModel>();
		builder.Services.AddSingleton<ExercisePickerPage>();
		builder.Services.AddTransient<CustomExercisePage>();
		builder.Services.AddSingleton<WorkoutPage>();
		builder.Services.AddTransient<SetLoggerPage>();
		builder.Services.AddTransient<WorkoutHistoryPage>();
		builder.Services.AddSingleton<AppShell>();
		configureTestServices?.Invoke(builder.Services);

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
