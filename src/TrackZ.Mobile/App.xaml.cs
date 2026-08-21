using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile;

public partial class App : Application
{
	private readonly IServiceProvider _services;
	private readonly AuthGateCoordinator _authentication;
	private readonly IWorkoutSyncLifecycle _synchronization;
	private readonly CancellationTokenSource _lifetime = new();
	private Window? _window;
	private bool _initializationStarted;
	private bool _syncIsRunning;

	public App(
		IServiceProvider services,
		AuthGateCoordinator authentication,
		IWorkoutSyncLifecycle synchronization)
	{
		InitializeComponent();
		_services = services;
		_authentication = authentication;
		_synchronization = synchronization;
	}

	internal Task Initialization { get; private set; } = Task.CompletedTask;

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_services.GetRequiredService<AuthGatePage>());
		_window = window;
		_authentication.Changed += OnAuthenticationChanged;
		window.Resumed += OnWindowResumed;
		window.Stopped += OnWindowStopped;
		window.Destroying += OnWindowDestroying;
		if (!_initializationStarted)
		{
			_initializationStarted = true;
			Initialization = _authentication.InitializeAsync(_lifetime.Token);
		}
		return window;
	}

	internal Window CreateTestWindow() => CreateWindow(null);

	private void OnAuthenticationChanged(object? sender, AuthGateSnapshot snapshot) =>
		RunOnUiThread(() => ActivateRoot(snapshot));

	private void ActivateRoot(AuthGateSnapshot snapshot)
	{
		if (_window is null) return;
		Page root = snapshot.State switch
		{
			AuthGateState.SignedIn => _services.GetRequiredService<AppShell>(),
			AuthGateState.SignedOut => _services.GetRequiredService<AuthShell>(),
			_ => _services.GetRequiredService<AuthGatePage>()
		};

		if (ReferenceEquals(_window.Page, root)) return;
		if (_window.Page is AppShell) StopSynchronization();
		_window.Page = root;
		if (root is AppShell) StartSynchronization();
	}

	private void OnWindowResumed(object? sender, EventArgs eventArgs)
	{
		if (_window?.Page is not AppShell) return;
		_synchronization.Resume();
		_syncIsRunning = true;
	}

	private void OnWindowStopped(object? sender, EventArgs eventArgs) => StopSynchronization();

	private void OnWindowDestroying(object? sender, EventArgs eventArgs)
	{
		_authentication.Changed -= OnAuthenticationChanged;
		_lifetime.Cancel();
		StopSynchronization();
		_window = null;
	}

	private void StartSynchronization()
	{
		if (_syncIsRunning) return;
		_synchronization.Start();
		_syncIsRunning = true;
	}

	private void StopSynchronization()
	{
		if (!_syncIsRunning) return;
		_synchronization.Stop();
		_syncIsRunning = false;
	}

	private void RunOnUiThread(Action action)
	{
		var dispatcher = _window?.Dispatcher;
		if (dispatcher?.IsDispatchRequired == true)
		{
			dispatcher.Dispatch(action);
			return;
		}
		action();
	}
}
