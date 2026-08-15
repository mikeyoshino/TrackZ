using Microsoft.Extensions.Logging;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;

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

		builder.Services.AddSingleton<IAccessTokenProvider, SecureStorageAccessTokenProvider>();
		builder.Services.AddSingleton(services => new HttpClient(
			new BearerTokenHandler(services.GetRequiredService<IAccessTokenProvider>())
			{
				InnerHandler = new HttpClientHandler()
			})
		{
			BaseAddress = new Uri("https://api.trackz.app"),
			Timeout = TimeSpan.FromSeconds(30)
		});
		builder.Services.AddSingleton<TrackZExerciseApiClient>();
		builder.Services.AddSingleton<IExerciseCatalogApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<ICustomExerciseApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<IExerciseImageApi>(services => services.GetRequiredService<TrackZExerciseApiClient>());
		builder.Services.AddSingleton<IExerciseThumbnailCache>(services => new AuthenticatedExerciseThumbnailCache(
			services.GetRequiredService<HttpClient>(),
			Path.Combine(FileSystem.AppDataDirectory, "exercise-thumbnails")));
		builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
		builder.Services.AddSingleton<IClock, SystemClock>();
		builder.Services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
		builder.Services.AddSingleton<IExerciseFileStore, LocalExerciseFileStore>();
		builder.Services.AddSingleton(services => new ExerciseCache(
			Path.Combine(FileSystem.AppDataDirectory, "exercise-catalog.db")));
		builder.Services.AddSingleton<CustomExerciseImageService>();
		builder.Services.AddSingleton<LocalExerciseImageImporter>();
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
