using Microsoft.Extensions.Logging;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
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
		builder.Services.AddSingleton<TrackZIdentityApiClient>();
		builder.Services.AddSingleton<TrackZSyncApiClient>();
		builder.Services.AddSingleton<ISyncApi>(services => services.GetRequiredService<TrackZSyncApiClient>());
		builder.Services.AddSingleton<IExerciseCatalogApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
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
		builder.Services.AddSingleton<IRetryDelay, SystemRetryDelay>();
		builder.Services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
		builder.Services.AddSingleton<ILocalExerciseImagePicker, MauiLocalExerciseImagePicker>();
		builder.Services.AddSingleton<IExerciseFileStore, LocalExerciseFileStore>();
		builder.Services.AddSingleton(services => new ExerciseCache(
			Path.Combine(FileSystem.AppDataDirectory, "exercise-catalog.db")));
		builder.Services.AddSingleton(services => new TrackZLocalDatabase(
			Path.Combine(FileSystem.AppDataDirectory, "workouts.db")));
		builder.Services.AddSingleton<LocalWorkoutRepository>();
		builder.Services.AddSingleton<ILocalWorkoutRepository>(services =>
			services.GetRequiredService<LocalWorkoutRepository>());
		builder.Services.AddSingleton<OutboxRepository>();
		builder.Services.AddSingleton<ActiveWorkoutCoordinator>();
		builder.Services.AddSingleton<SyncCoordinator>();
		builder.Services.AddSingleton<ConflictResolution>();
		builder.Services.AddSingleton<CustomExerciseImageService>();
		builder.Services.AddSingleton<LocalExerciseImageImporter>();
		builder.Services.AddSingleton<LocalExerciseImageSelectionCoordinator>();
		builder.Services.AddTransient<ExercisePickerViewModel>();
		builder.Services.AddTransient<CustomExerciseViewModel>();
		builder.Services.AddSingleton<ExercisePickerPage>();
		builder.Services.AddTransient<CustomExercisePage>();
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
