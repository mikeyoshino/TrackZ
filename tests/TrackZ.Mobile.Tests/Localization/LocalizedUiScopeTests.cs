using Microsoft.Extensions.DependencyInjection.Extensions;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Localization;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Localization;

public sealed class LocalizedUiScopeTests
{
    public static TheoryData<Type> EventSubscribingLocalizedViewModels => new()
    {
        typeof(TrainTodayViewModel),
        typeof(WorkoutViewModel),
        typeof(ExercisePickerViewModel),
        typeof(WorkoutHistoryViewModel),
        typeof(WorkoutHistoryDetailViewModel),
        typeof(ProgressDashboardViewModel)
    };

    [Theory]
    [MemberData(nameof(EventSubscribingLocalizedViewModels))]
    public void Event_subscribing_localized_view_models_participate_in_scope_disposal(Type viewModelType)
    {
        Assert.True(
            typeof(IDisposable).IsAssignableFrom(viewModelType),
            $"{viewModelType.Name} must be disposed with its localized UI scope.");
    }

    [Fact]
    public void New_localized_scope_recreates_text_while_preserving_root_state_services()
    {
        using var app = MauiProgram.CreateMauiApp(appLanguageStore: new MemoryLanguageStore());
        var manager = app.Services.GetRequiredService<LocalizedUiScopeManager>();
        using var first = manager.CreateCandidate();
        using var second = manager.CreateCandidate();

        Assert.NotSame(
            first.Services.GetRequiredService<WorkoutTextSet>(),
            second.Services.GetRequiredService<WorkoutTextSet>());
        Assert.NotSame(
            first.Services.GetRequiredService<MobileTextSet>(),
            second.Services.GetRequiredService<MobileTextSet>());
        Assert.Same(
            first.Services.GetRequiredService<IAccountSessionBoundary>(),
            second.Services.GetRequiredService<IAccountSessionBoundary>());
    }

    [Fact]
    public void Language_changer_is_one_root_instance_shared_with_localized_profile()
    {
        using var app = MauiProgram.CreateMauiApp(services =>
        {
            services.RemoveAll<ILocalizedUiHost>();
            services.AddSingleton<ILocalizedUiHost, NoopLocalizedUiHost>();
            services.RemoveAll<AuthGateCoordinator>();
            services.AddSingleton(new AuthGateCoordinator(
                new MobileTokenStore(new MemoryTokenStorage()),
                new NoopIdentitySession(),
                new NoopPrivateDataCleaner(),
                new AccountSessionBoundary(),
                new FixedDeviceName(),
                new OnlineConnectivity(),
                TimeProvider.System));
        }, new MemoryLanguageStore());
        var concrete = app.Services.GetRequiredService<MauiAppLanguageChanger>();

        Assert.Same(concrete, app.Services.GetRequiredService<IAppLanguageChanger>());

        using var candidate = app.Services
            .GetRequiredService<LocalizedUiScopeManager>()
            .CreateCandidate();
        Assert.Same(
            concrete,
            candidate.Services.GetRequiredService<IAppLanguageChanger>());
    }

    [Fact]
    public void Root_tab_route_capture_and_restore_uses_the_selected_shell_content()
    {
        var original = CreateShell("history");
        Assert.Equal("history", App.GetCurrentRootTabRoute(original));

        var replacement = CreateShell("train");
        App.RestoreRootTabRoute(replacement, "history");

        Assert.Equal("history", App.GetCurrentRootTabRoute(replacement));
    }

    private static Shell CreateShell(string selectedRoute)
    {
        var train = new ShellContent { Route = "train", Content = new ContentPage() };
        var history = new ShellContent { Route = "history", Content = new ContentPage() };
        var trainTab = new Tab { Route = "train-tab", Items = { train } };
        var historyTab = new Tab { Route = "history-tab", Items = { history } };
        var tabs = new TabBar { Route = "root", Items = { trainTab, historyTab } };
        var shell = new Shell { Items = { tabs } };
        App.RestoreRootTabRoute(shell, selectedRoute);
        return shell;
    }

    [Fact]
    public void Activating_a_candidate_returns_the_previous_scope_for_exact_disposal()
    {
        using var app = MauiProgram.CreateMauiApp(appLanguageStore: new MemoryLanguageStore());
        var manager = app.Services.GetRequiredService<LocalizedUiScopeManager>();
        using var first = manager.CreateCandidate();
        using var second = manager.CreateCandidate();

        Assert.Null(manager.Activate(first));
        Assert.Same(first, manager.Activate(second));
        Assert.Same(second.Services, manager.ActiveServices);
    }

    [Fact]
    public void Localized_auth_page_is_recreated_for_each_candidate_scope()
    {
        using var app = MauiProgram.CreateMauiApp(appLanguageStore: new MemoryLanguageStore());
        var manager = app.Services.GetRequiredService<LocalizedUiScopeManager>();
        using var first = manager.CreateCandidate();
        using var second = manager.CreateCandidate();

        Assert.NotSame(
            first.Services.GetRequiredService<AuthGatePage>(),
            second.Services.GetRequiredService<AuthGatePage>());
    }

    [Fact]
    public void Scoped_route_factory_resolves_from_the_current_active_scope()
    {
        using var app = MauiProgram.CreateMauiApp(appLanguageStore: new MemoryLanguageStore());
        var manager = app.Services.GetRequiredService<LocalizedUiScopeManager>();
        using var first = manager.CreateCandidate();
        using var second = manager.CreateCandidate();
        var route = new ScopedRouteFactory<AuthGatePage>(manager);

        manager.Activate(first);
        var firstPage = route.GetOrCreate();
        manager.Activate(second);
        var secondPage = route.GetOrCreate();

        Assert.Same(first.Services.GetRequiredService<AuthGatePage>(), firstPage);
        Assert.Same(second.Services.GetRequiredService<AuthGatePage>(), secondPage);
        Assert.NotSame(firstPage, secondPage);
    }

    [Fact]
    public void Ui_host_prepares_the_auth_root_from_one_candidate_scope()
    {
        using var app = MauiProgram.CreateMauiApp(services =>
        {
            services.RemoveAll<IIdentitySessionApi>();
            services.AddSingleton<IIdentitySessionApi, NoopIdentitySession>();
            services.RemoveAll<IMobilePrivateDataCleaner>();
            services.AddSingleton<IMobilePrivateDataCleaner, NoopPrivateDataCleaner>();
            services.RemoveAll<IConnectivityService>();
            services.AddSingleton<IConnectivityService, OnlineConnectivity>();
            services.RemoveAll<IWorkoutSyncLifecycle>();
            services.AddSingleton<IWorkoutSyncLifecycle, NoopSyncLifecycle>();
        }, new MemoryLanguageStore());
        var host = app.Services.GetRequiredService<ILocalizedUiHost>();

        using var installation = host.Prepare(new AuthGateSnapshot(AuthGateState.CheckingSession));

        Assert.Same(
            installation.Scope.Services.GetRequiredService<AuthGatePage>(),
            installation.Root);
    }

    [Fact]
    public void Disposing_a_localized_scope_releases_root_event_subscriptions()
    {
        var boundary = new CountingSessionBoundary();
        using var app = MauiProgram.CreateMauiApp(services =>
        {
            services.RemoveAll<IAccountSessionBoundary>();
            services.AddSingleton<IAccountSessionBoundary>(boundary);
            services.RemoveAll<IProgressSnapshotSource>();
            services.AddSingleton<IProgressSnapshotSource, EmptyProgressSource>();
            services.RemoveAll<IConnectivityService>();
            services.AddSingleton<IConnectivityService, OnlineConnectivity>();
            services.RemoveAll<IWeightUnitPreference>();
            services.AddSingleton<IWeightUnitPreference, FixedWeightUnit>();
        }, new MemoryLanguageStore());
        var manager = app.Services.GetRequiredService<LocalizedUiScopeManager>();
        var candidate = manager.CreateCandidate();

        _ = candidate.Services.GetRequiredService<ProgressDashboardViewModel>();
        Assert.Equal(1, boundary.Subscriptions);

        candidate.Dispose();

        Assert.Equal(0, boundary.Subscriptions);
    }

    private sealed class MemoryLanguageStore : IAppLanguageStore
    {
        public AppLanguage Read() => AppLanguage.Thai;
        public void Write(AppLanguage language) { }
    }

    private sealed class NoopIdentitySession : IIdentitySessionApi
    {
        public Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedDeviceName : IDeviceNameProvider
    {
        public string DeviceName => "tests";
    }

    private sealed class NoopLocalizedUiHost : ILocalizedUiHost
    {
        public string? CurrentRootTabRoute => "train";
        public LocalizedUiInstallation Prepare(AuthGateSnapshot snapshot) => throw new NotSupportedException();
        public Task InstallAsync(LocalizedUiInstallation installation, string? rootTabRoute, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class NoopSyncLifecycle : IWorkoutSyncLifecycle
    {
        public void Start() { }
        public void Resume() { }
        public void Stop() { }
    }

    private sealed class CountingSessionBoundary : IAccountSessionBoundary
    {
        private readonly AccountSessionBoundary _inner = new();
        public int Subscriptions { get; private set; }

        public event EventHandler? SessionReset
        {
            add { Subscriptions++; _inner.SessionReset += value; }
            remove { Subscriptions--; _inner.SessionReset -= value; }
        }

        public AccountSessionGeneration Capture() => _inner.Capture();
        public bool IsCancellationRequested(AccountSessionGeneration generation) =>
            _inner.IsCancellationRequested(generation);
        public AccountSessionCancellationLease CreateCancellationLease(
            AccountSessionGeneration generation,
            CancellationToken cancellationToken = default) =>
            _inner.CreateCancellationLease(generation, cancellationToken);
        public bool TryStartSessionPhase(
            AccountSessionGeneration generation,
            Action phase,
            CancellationToken cancellationToken = default) =>
            _inner.TryStartSessionPhase(generation, phase, cancellationToken);
        public Task<bool> TryCommitAsync(
            AccountSessionGeneration generation,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default) =>
            _inner.TryCommitAsync(generation, mutation, cancellationToken);
        public Task ResetAsync(
            Func<CancellationToken, Task> reset,
            CancellationToken cancellationToken = default) =>
            _inner.ResetAsync(reset, cancellationToken);
        public Task<bool> TryResetAsync(
            AccountSessionGeneration generation,
            Func<CancellationToken, Task> reset,
            CancellationToken cancellationToken = default) =>
            _inner.TryResetAsync(generation, reset, cancellationToken);
    }

    private sealed class EmptyProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(null);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException());
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException());
    }

    private sealed class FixedWeightUnit : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
    }
}
