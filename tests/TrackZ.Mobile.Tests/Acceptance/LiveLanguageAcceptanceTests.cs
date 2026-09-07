using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Dispatching;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Localization;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Acceptance;

public sealed class LiveLanguageAcceptanceTests
{
    private static readonly Guid ExerciseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomExerciseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Fresh_install_switch_recreation_and_Thai_reversal_preserve_root_state_and_names()
    {
        var culture = CultureSnapshot.Capture();
        var root = Path.Combine(Path.GetTempPath(), $"trackz-language-acceptance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var languageStore = new MemoryLanguageStore();
        var originalDispatcher = DispatcherProvider.Current;
        try
        {
            DispatcherProvider.SetCurrent(new HeadlessDispatcherProvider());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            await using (var first = await Fixture.CreateAsync(root, languageStore))
            {
                Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentUICulture!.Name);
                AppLanguageCulture.Apply(languageStore.Read());
                using (var signedOut = first.Host.Prepare(new AuthGateSnapshot(AuthGateState.SignedOut)))
                {
                    var signIn = signedOut.Scope.Services.GetRequiredService<SignInPage>();
                    Assert.Equal("ติดตามการฝึกง่ายขึ้น\nเห็นผลลัพธ์ชัดขึ้น", signIn.Form.Text.SignInTitle);
                    Assert.Equal("อีเมล", signIn.Form.Text.EmailAccessibilityLabel);
                }

                var thaiShell = Assert.IsType<AppShell>(first.Host.Root);
                Assert.Equal("ผลการฝึก", Tabs(thaiShell)[2].Title);
                Assert.Equal("คุณ", Tabs(thaiShell)[3].Title);
                App.RestoreRootTabRoute(thaiShell, "you");
                var active = (await first.Repository.GetActiveAsync())!;
                var generation = first.Boundary.Capture();
                var sync = first.App.Services.GetRequiredService<SyncCoordinator>();
                var units = first.App.Services.GetRequiredService<IWeightUnitPreference>();
                units.Set(WeightDisplayUnit.Pounds);

                var profile = first.Host.ActiveServices.GetRequiredService<ProfileViewModel>();
                await profile.ChangeLanguageCommand.ExecuteAsync(AppLanguage.English);

                var englishShell = Assert.IsType<AppShell>(first.Host.Root);
                Assert.Equal("Progress", Tabs(englishShell)[2].Title);
                Assert.Equal("You", Tabs(englishShell)[3].Title);
                Assert.Equal("you", App.GetCurrentRootTabRoute(englishShell));
                Assert.Equal(active.Id, (await first.Repository.GetActiveAsync())!.Id);
                Assert.Equal(generation, first.Boundary.Capture());
                Assert.Same(sync, first.App.Services.GetRequiredService<SyncCoordinator>());
                Assert.Same(units, first.App.Services.GetRequiredService<IWeightUnitPreference>());
                Assert.Equal(WeightDisplayUnit.Pounds, units.Current);
                Assert.True(first.Host.ActiveServices.GetRequiredService<ProfileViewModel>().Languages[1].IsSelected);
                await AssertExerciseNamesAreUnchangedAsync(first);
            }

            Assert.Equal(AppLanguage.English, languageStore.Read());

            await using (var recreated = await Fixture.CreateAsync(root, languageStore))
            {
                Assert.Equal("en-US", CultureInfo.DefaultThreadCurrentUICulture!.Name);
                var englishShell = Assert.IsType<AppShell>(recreated.Host.Root);
                Assert.Equal("Progress", Tabs(englishShell)[2].Title);
                App.RestoreRootTabRoute(englishShell, "you");

                var profile = recreated.Host.ActiveServices.GetRequiredService<ProfileViewModel>();
                await profile.ChangeLanguageCommand.ExecuteAsync(AppLanguage.Thai);

                var thaiShell = Assert.IsType<AppShell>(recreated.Host.Root);
                Assert.Equal("ผลการฝึก", Tabs(thaiShell)[2].Title);
                Assert.Equal("คุณ", Tabs(thaiShell)[3].Title);
                Assert.Equal("you", App.GetCurrentRootTabRoute(thaiShell));
                var custom = recreated.Host.ActiveServices.GetRequiredService<CustomExerciseViewModel>();
                Assert.Equal("หน้าอก", custom.BodyPartOptions.Single(option => option.Value == BodyPart.Chest).Label);
                Assert.Equal(
                    "น้ำหนักที่ยก + จำนวนครั้ง",
                    custom.TrackingModeOptions.Single(option => option.Value == TrackingMode.Weighted).Label);
                await AssertExerciseNamesAreUnchangedAsync(recreated);
            }

            Assert.Equal(AppLanguage.Thai, languageStore.Read());
        }
        finally
        {
            DispatcherProvider.SetCurrent(originalDispatcher);
            culture.Restore();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertExerciseNamesAreUnchangedAsync(Fixture fixture)
    {
        var picker = fixture.Host.ActiveServices.GetRequiredService<ExercisePickerViewModel>();
        await picker.LoadAsync();
        var names = picker.Exercises.Select(exercise => exercise.Name).ToArray();
        Assert.Equal(2, names.Length);
        Assert.Contains("API Bench Press", names);
        Assert.Contains("ท่าที่ผู้ใช้ตั้งเอง", names);
    }

    private static IReadOnlyList<ShellSection> Tabs(AppShell shell) =>
        Assert.IsType<TabBar>(Assert.Single(shell.Items)).Items.ToArray();

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            MauiApp app,
            AcceptanceUiHost host,
            LocalWorkoutRepository repository,
            IAccountSessionBoundary boundary)
        {
            App = app;
            Host = host;
            Repository = repository;
            Boundary = boundary;
        }

        public MauiApp App { get; }
        public AcceptanceUiHost Host { get; }
        public LocalWorkoutRepository Repository { get; }
        public IAccountSessionBoundary Boundary { get; }

        public static async Task<Fixture> CreateAsync(string root, IAppLanguageStore languageStore)
        {
            var cache = new ExerciseCache(Path.Combine(root, "exercises.db"));
            var database = new TrackZLocalDatabase(Path.Combine(root, "workouts.db"));
            var repository = new LocalWorkoutRepository(database);
            var boundary = new AccountSessionBoundary();
            await cache.ReplaceAllAsync(
            [
                new ExerciseSummaryDto(
                    ExerciseId, "API Bench Press", BodyPart.Chest, TrackingMode.Weighted,
                    null, DateTimeOffset.UtcNow, null, null, false),
                new ExerciseSummaryDto(
                    CustomExerciseId, "ท่าที่ผู้ใช้ตั้งเอง", BodyPart.Back, TrackingMode.Bodyweight,
                    null, DateTimeOffset.UtcNow, null, null, true)
            ], DateTimeOffset.UtcNow);
            var active = await repository.GetActiveAsync();
            if (active is null)
            {
                active = await new ActiveWorkoutCoordinator(repository, boundary, new SystemClock())
                    .StartAsync([new WorkoutExerciseSelection(ExerciseId, TrackingMode.Weighted)]);
                await new ActiveWorkoutCoordinator(repository, boundary, new SystemClock())
                    .SaveSetAsync(ExerciseId, new LocalSet(70.125m, null, 8));
            }

            var tokenStorage = new AuthenticatedTokenStorage();
            var gate = new AuthGateCoordinator(
                new MobileTokenStore(tokenStorage),
                new NoopIdentitySession(),
                new NoopPrivateDataCleaner(),
                boundary,
                new FixedDeviceName(),
                new OfflineConnectivity(),
                TimeProvider.System);
            await gate.InitializeAsync();
            Assert.Equal(AuthGateState.SignedIn, gate.Snapshot.State);

            var preferences = new MemoryWorkoutPreferences();
            var app = MauiProgram.CreateMauiApp(services =>
            {
                services.RemoveAll<AuthGateCoordinator>();
                services.AddSingleton(gate);
                services.RemoveAll<IAccountSessionBoundary>();
                services.AddSingleton<IAccountSessionBoundary>(boundary);
                services.RemoveAll<IConnectivityService>();
                services.AddSingleton<IConnectivityService, OfflineConnectivity>();
                services.RemoveAll<IUiDispatcher>();
                services.AddSingleton<IUiDispatcher, InlineUiDispatcher>();
                services.RemoveAll<ExerciseCache>();
                services.AddSingleton(cache);
                services.RemoveAll<TrackZLocalDatabase>();
                services.AddSingleton(database);
                services.RemoveAll<LocalWorkoutRepository>();
                services.AddSingleton(repository);
                services.RemoveAll<ILocalWorkoutRepository>();
                services.AddSingleton<ILocalWorkoutRepository>(repository);
                services.RemoveAll<IExerciseThumbnailCache>();
                services.AddSingleton<IExerciseThumbnailCache, NullThumbnailCache>();
                services.RemoveAll<IProgressSnapshotSource>();
                services.AddSingleton<IProgressSnapshotSource, EmptyProgressSource>();
                services.RemoveAll<IWorkoutPreferenceStore>();
                services.AddSingleton<IWorkoutPreferenceStore>(preferences);
                services.RemoveAll<IWeightUnitPreference>();
                services.AddSingleton<IWeightUnitPreference, WeightUnitPreference>();
                services.RemoveAll<ILocalizedUiHost>();
                services.AddSingleton<AcceptanceUiHost>();
                services.AddSingleton<ILocalizedUiHost>(provider =>
                    provider.GetRequiredService<AcceptanceUiHost>());
            }, languageStore);
            // Load app-level styles before constructing pages; do not depend on another test's Application.Current.
            _ = app.Services.GetRequiredService<Microsoft.Maui.IApplication>();
            var host = app.Services.GetRequiredService<AcceptanceUiHost>();
            host.Initialize(gate.Snapshot);
            return new Fixture(app, host, repository, boundary);
        }

        public ValueTask DisposeAsync()
        {
            App.Dispose();
            return ValueTask.CompletedTask;
        }
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

    private sealed class AcceptanceUiHost(
        LocalizedUiScopeManager scopes) : ILocalizedUiHost, IDisposable
    {
        public Page? Root { get; private set; }
        public IServiceProvider ActiveServices => scopes.ActiveServices;
        public string? CurrentRootTabRoute => App.GetCurrentRootTabRoute(Root as Shell);

        public void Initialize(AuthGateSnapshot snapshot)
        {
            var installation = Prepare(snapshot);
            Root = installation.Root;
            scopes.Activate(installation.Scope)?.Dispose();
        }

        public LocalizedUiInstallation Prepare(AuthGateSnapshot snapshot)
        {
            var scope = scopes.CreateCandidate();
            try
            {
                Page root = snapshot.State switch
                {
                    AuthGateState.SignedIn => scope.Services.GetRequiredService<AppShell>(),
                    AuthGateState.SignedOut => scope.Services.GetRequiredService<AuthShell>(),
                    _ => scope.Services.GetRequiredService<AuthGatePage>()
                };
                return new LocalizedUiInstallation(scope, root);
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        public Task InstallAsync(
            LocalizedUiInstallation installation,
            string? rootTabRoute,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            App.RestoreRootTabRoute(installation.Root as Shell, rootTabRoute);
            Root = installation.Root;
            scopes.Activate(installation.Scope)?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            scopes.Deactivate()?.Dispose();
            Root = null;
        }
    }

    private sealed class MemoryLanguageStore : IAppLanguageStore
    {
        private AppLanguage? _value;
        public AppLanguage Read() => _value ?? AppLanguage.Thai;
        public void Write(AppLanguage language) => _value = language;
    }

    private sealed class MemoryWorkoutPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
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

    private sealed class NoopIdentitySession : IIdentitySessionApi
    {
        public Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IPreparedIdentityLogout> PrepareLogoutAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PreparedIdentityLogout.None);
    }

    private sealed class NoopPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedDeviceName : IDeviceNameProvider
    {
        public string DeviceName => "acceptance";
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class NullThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(null);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException();
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException();
    }

    private sealed record CultureSnapshot(
        CultureInfo Current,
        CultureInfo CurrentUi,
        CultureInfo? Default,
        CultureInfo? DefaultUi)
    {
        public static CultureSnapshot Capture() => new(
            CultureInfo.CurrentCulture,
            CultureInfo.CurrentUICulture,
            CultureInfo.DefaultThreadCurrentCulture,
            CultureInfo.DefaultThreadCurrentUICulture);

        public void Restore()
        {
            CultureInfo.CurrentCulture = Current;
            CultureInfo.CurrentUICulture = CurrentUi;
            CultureInfo.DefaultThreadCurrentCulture = Default;
            CultureInfo.DefaultThreadCurrentUICulture = DefaultUi;
        }
    }
}
