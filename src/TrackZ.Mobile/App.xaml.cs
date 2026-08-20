using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile;

public partial class App : Application
{
	private readonly AppShell _shell;
	private readonly IWorkoutSyncLifecycle _synchronization;

	public App(AppShell shell, IWorkoutSyncLifecycle synchronization)
	{
		InitializeComponent();
		_shell = shell;
		_synchronization = synchronization;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_shell);
		window.Created += (_, _) => _synchronization.Start();
		window.Resumed += (_, _) => _synchronization.Resume();
		window.Stopped += (_, _) => _synchronization.Stop();
		window.Destroying += (_, _) => _synchronization.Stop();
		_synchronization.Start();
		return window;
	}
}
