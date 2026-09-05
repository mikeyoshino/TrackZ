using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Localization;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile;

public partial class App : Application, ILocalizedUiHost
{
	private readonly AuthGateCoordinator _authentication;
	private readonly IWorkoutSyncLifecycle _synchronization;
	private readonly LocalizedUiScopeManager _localizedScopes;
	private readonly CancellationTokenSource _lifetime = new();
	private Window? _window;
	private LocalizedUiScope? _localizedUi;
	private bool _initializationStarted;
	private bool _syncIsRunning;

	public App(
		AuthGateCoordinator authentication,
		IWorkoutSyncLifecycle synchronization,
		LocalizedUiScopeManager localizedScopes)
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
		_authentication = authentication;
		_synchronization = synchronization;
		_localizedScopes = localizedScopes;
	}

	internal Task Initialization { get; private set; } = Task.CompletedTask;

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var installation = Prepare(_authentication.Snapshot);
		_localizedUi = installation.Scope;
		_localizedScopes.Activate(installation.Scope)?.Dispose();
		var window = new Window(installation.Root);
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
		if (_window is null || _localizedUi is null) return;
		var root = ResolveRoot(_localizedUi, snapshot);

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
		_localizedScopes.Deactivate()?.Dispose();
		_localizedUi = null;
		_window = null;
	}

	public string? CurrentRootTabRoute =>
		GetCurrentRootTabRoute(_window?.Page as Shell);

	public LocalizedUiInstallation Prepare(AuthGateSnapshot snapshot)
	{
		var candidate = _localizedScopes.CreateCandidate();
		try
		{
			return new LocalizedUiInstallation(candidate, ResolveRoot(candidate, snapshot));
		}
		catch
		{
			candidate.Dispose();
			throw;
		}
	}

	public async Task InstallAsync(
		LocalizedUiInstallation installation,
		string? rootTabRoute,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(installation);
		cancellationToken.ThrowIfCancellationRequested();
		if (_window is null)
			throw new InvalidOperationException("The application window is not available.");

		await RunOnUiThreadAsync(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_window.Page is AppShell) StopSynchronization();
			_window.Page = installation.Root;
			RestoreRootTabRoute(installation.Root as Shell, rootTabRoute);
			_localizedUi = installation.Scope;
			_localizedScopes.Activate(installation.Scope)?.Dispose();
			if (installation.Root is AppShell) StartSynchronization();
		}, cancellationToken);
	}

	internal static string? GetCurrentRootTabRoute(Shell? shell) =>
		shell?.CurrentItem?.CurrentItem?.CurrentItem?.Route
		?? shell?.CurrentItem?.CurrentItem?.Route
		?? shell?.CurrentItem?.Route;

	internal static void RestoreRootTabRoute(Shell? shell, string? route)
	{
		if (shell is null || string.IsNullOrWhiteSpace(route)) return;
		foreach (var item in shell.Items)
		foreach (var section in item.Items)
		foreach (var content in section.Items)
		{
			if (!string.Equals(content.Route, route, StringComparison.Ordinal)) continue;
			section.CurrentItem = content;
			item.CurrentItem = section;
			shell.CurrentItem = item;
			return;
		}
	}

	private static Page ResolveRoot(LocalizedUiScope scope, AuthGateSnapshot snapshot) =>
		snapshot.State switch
		{
			AuthGateState.SignedIn => scope.Services.GetRequiredService<AppShell>(),
			AuthGateState.SignedOut => scope.Services.GetRequiredService<AuthShell>(),
			_ => scope.Services.GetRequiredService<AuthGatePage>()
		};

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

	private async Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var dispatcher = _window?.Dispatcher;
		if (dispatcher?.IsDispatchRequired != true)
		{
			action();
			return;
		}

		var completion = new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
		if (!dispatcher.Dispatch(() =>
		{
			try
			{
				action();
				completion.SetResult();
			}
			catch (Exception error)
			{
				completion.SetException(error);
			}
		}))
		{
			throw new InvalidOperationException("The localized UI dispatch could not be scheduled.");
		}

		await completion.Task;
	}
}
