using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Services;

namespace TrackZ.Mobile;

public partial class App : Application
{
	private readonly AppShell _shell;

	public App(AppShell shell, CustomExerciseImageService synchronization)
	{
		InitializeComponent();
		_shell = shell;
		_ = SynchronizePendingAsync(synchronization);
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_shell);
	}

	private static async Task SynchronizePendingAsync(CustomExerciseImageService synchronization)
	{
		try
		{
			await synchronization.SynchronizePendingAsync();
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException or MobileApiException)
		{
			// The durable outbox remains authoritative and reconnect will retry it.
		}
	}
}
