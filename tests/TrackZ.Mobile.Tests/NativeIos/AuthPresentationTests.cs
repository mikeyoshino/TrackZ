using System.Text;
using System.Reflection;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Dispatching;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class AuthPresentationTests
{
    [Fact]
    public async Task Fresh_launch_never_resolves_protected_shell_before_sign_in()
    {
        using var scope = TestApp.Create();
        var application = scope.App.Services.GetRequiredService<App>();

        var window = application.CreateTestWindow();
        await application.Initialization;

        Assert.IsType<AuthShell>(window.Page);
        Assert.Equal(0, scope.Probe.ResolutionCount);
        Assert.Equal(0, scope.Sync.StartCount);
    }

    [Fact]
    public async Task Signed_in_bootstrap_activates_protected_shell_and_starts_sync_once()
    {
        using var scope = TestApp.Create();
        await scope.App.Services.GetRequiredService<MobileTokenStore>().SaveAsync(CreateToken(DateTimeOffset.UtcNow.AddHours(1)), "refresh");
        var application = scope.App.Services.GetRequiredService<App>();

        var window = application.CreateTestWindow();
        await application.Initialization;

        Assert.IsType<AppShell>(window.Page);
        Assert.Equal(1, scope.Probe.ResolutionCount);
        Assert.Equal(1, scope.Sync.StartCount);
    }

    [Fact]
    public async Task Sign_out_returns_to_auth_shell_and_stops_sync()
    {
        using var scope = TestApp.Create();
        await scope.App.Services.GetRequiredService<MobileTokenStore>().SaveAsync(CreateToken(DateTimeOffset.UtcNow.AddHours(1)), "refresh");
        var application = scope.App.Services.GetRequiredService<App>();
        var window = application.CreateTestWindow();
        await application.Initialization;

        await scope.App.Services.GetRequiredService<AuthGateCoordinator>().SignOutAsync();

        Assert.IsType<AuthShell>(window.Page);
        Assert.Equal(1, scope.Sync.StopCount);
    }

    [Fact]
    public async Task Resume_while_signed_out_does_not_resume_sync()
    {
        using var scope = TestApp.Create();
        var application = scope.App.Services.GetRequiredService<App>();
        var window = application.CreateTestWindow();
        await application.Initialization;

        typeof(App).GetMethod("OnWindowResumed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(application, [window, EventArgs.Empty]);

        Assert.Equal(0, scope.Sync.ResumeCount);
    }

    [Fact]
    public async Task Repeated_terminal_state_does_not_replace_the_active_root()
    {
        using var scope = TestApp.Create();
        var application = scope.App.Services.GetRequiredService<App>();
        var window = application.CreateTestWindow();
        await application.Initialization;
        var root = window.Page;

        await scope.App.Services.GetRequiredService<AuthGateCoordinator>().SignOutAsync();

        Assert.Same(root, window.Page);
    }

    [Fact]
    public void Auth_fields_are_exactly_50_points_and_actions_are_at_least_44_points()
    {
        using var scope = TestApp.Create();

        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var create = scope.App.Services.GetRequiredService<CreateAccountPage>();
        VisualElement[] controls =
        {
            signIn.EmailEntry, signIn.PasswordEntry, signIn.SubmitButton, signIn.SwitchModeButton,
            create.EmailEntry, create.PasswordEntry, create.SubmitButton, create.SwitchModeButton
        };

        Assert.All(controls, control => Assert.True(control.MinimumHeightRequest >= 44));
        Assert.Equal(50, signIn.EmailField.MinimumHeightRequest);
        Assert.Equal(50, signIn.PasswordField.MinimumHeightRequest);
        Assert.Equal(50, signIn.EmailEntry.MinimumHeightRequest);
        Assert.Equal(50, signIn.PasswordEntry.MinimumHeightRequest);
        Assert.Equal(50, create.EmailField.MinimumHeightRequest);
        Assert.Equal(50, create.PasswordField.MinimumHeightRequest);
        Assert.Equal(50, create.EmailEntry.MinimumHeightRequest);
        Assert.Equal(50, create.PasswordEntry.MinimumHeightRequest);
    }

    [Fact]
    public void Auth_pages_share_the_gate_but_each_receive_a_transient_form()
    {
        using var scope = TestApp.Create();

        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var create = scope.App.Services.GetRequiredService<CreateAccountPage>();

        Assert.Same(signIn.Gate, create.Gate);
        Assert.NotSame(signIn.Form, create.Form);
        Assert.Equal(AuthFormMode.SignIn, signIn.Form.Mode);
        Assert.Equal(AuthFormMode.CreateAccount, create.Form.Mode);
    }

    [Fact]
    public void English_culture_binds_real_auth_page_text_and_semantics()
    {
        using var culture = new UiCultureScope("en-US");
        using var scope = TestApp.Create();

        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var create = scope.App.Services.GetRequiredService<CreateAccountPage>();
        var gate = scope.App.Services.GetRequiredService<AuthGatePage>();

        Assert.Equal(AuthTextSet.English, signIn.Form.Text);
        Assert.Equal(AuthTextSet.English.SignInWelcomeBody, signIn.WelcomeBody.Text);
        Assert.Equal(AuthTextSet.English.CreateAccountWelcomeBody, create.WelcomeBody.Text);
        Assert.Equal(AuthTextSet.English.EmailAccessibilityLabel, SemanticProperties.GetDescription(signIn.EmailEntry));
        Assert.Equal(AuthTextSet.English.CheckingSessionAccessibilityLabel, SemanticProperties.GetDescription(gate.StatusIndicator));
    }

    [Fact]
    public void Thai_culture_binds_real_auth_page_text_and_semantics()
    {
        using var culture = new UiCultureScope("th-TH");
        using var scope = TestApp.Create();

        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var create = scope.App.Services.GetRequiredService<CreateAccountPage>();
        var gate = scope.App.Services.GetRequiredService<AuthGatePage>();

        Assert.Equal(AuthTextSet.Thai, signIn.Form.Text);
        Assert.Equal(AuthTextSet.Thai.SignInWelcomeBody, signIn.WelcomeBody.Text);
        Assert.Equal(AuthTextSet.Thai.CreateAccountWelcomeBody, create.WelcomeBody.Text);
        Assert.Equal(AuthTextSet.Thai.PasswordAccessibilityLabel, SemanticProperties.GetDescription(create.PasswordEntry));
        Assert.Equal(AuthTextSet.Thai.CheckingSessionAccessibilityLabel, SemanticProperties.GetDescription(gate.StatusIndicator));
    }

    private static string CreateToken(DateTimeOffset expiresAt)
    {
        static string Part(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("{\"alg\":\"none\"}")}.{Part($"{{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}")}.signature";
    }

    private sealed class TestApp : IDisposable
    {
        private readonly IDispatcherProvider _originalDispatcherProvider;

        private readonly string _root;

        private TestApp(MauiApp app, ProtectedShellProbe probe, RecordingSyncLifecycle sync, IDispatcherProvider originalDispatcherProvider, string root)
        {
            App = app;
            Probe = probe;
            Sync = sync;
            _originalDispatcherProvider = originalDispatcherProvider;
            _root = root;
        }

        public MauiApp App { get; }
        public ProtectedShellProbe Probe { get; }
        public RecordingSyncLifecycle Sync { get; }

        public static TestApp Create()
        {
            var original = DispatcherProvider.Current;
            DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
            var root = Path.Combine(Path.GetTempPath(), $"trackz-auth-presentation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var probe = new ProtectedShellProbe();
            var sync = new RecordingSyncLifecycle();
            var app = MauiProgram.CreateMauiApp(services =>
            {
                services.RemoveAll<IMobileTokenStorage>();
                services.AddSingleton<IMobileTokenStorage, MemoryTokenStorage>();
                services.RemoveAll<IMobilePrivateDataCleaner>();
                services.AddSingleton<IMobilePrivateDataCleaner, NoopPrivateDataCleaner>();
                services.RemoveAll<IConnectivityService>();
                services.AddSingleton<IConnectivityService, OfflineConnectivity>();
                services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
                services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
                services.AddSingleton(new TrackZLocalDatabase(Path.Combine(root, "workouts.db")));
                services.AddSingleton(new ProgressSnapshotCache(Path.Combine(root, "progress.json")));
                services.AddSingleton<IExerciseThumbnailCache, NullThumbnailCache>();
                services.AddSingleton<IWorkoutPreferenceStore, MemoryPreferences>();
                services.RemoveAll<IWorkoutSyncLifecycle>();
                services.AddSingleton<IWorkoutSyncLifecycle>(sync);
                services.RemoveAll<AppShell>();
                services.AddSingleton(probe);
                services.AddSingleton<AppShell>(provider =>
                {
                    probe.ResolutionCount++;
                    return ActivatorUtilities.CreateInstance<AppShell>(provider);
                });
            });
            return new TestApp(app, probe, sync, original, root);
        }

        public void Dispose()
        {
            App.Dispose();
            DispatcherProvider.SetCurrent(_originalDispatcherProvider);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }

    internal sealed class ProtectedShellProbe
    {
        public int ResolutionCount { get; set; }
    }

    private sealed class RecordingSyncLifecycle : IWorkoutSyncLifecycle
    {
        public int StartCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }
        public void Start() => StartCount++;
        public void Resume() => ResumeCount++;
        public void Stop() => StopCount++;
    }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) { _values.Remove(key); return Task.CompletedTask; }
    }

    private sealed class NoopPrivateDataCleaner : IMobilePrivateDataCleaner
    {
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class NullThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class MemoryPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class InlineDispatcherProvider : IDispatcherProvider
    {
        public IDispatcher GetForCurrentThread() => new InlineDispatcher();
    }

    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new InlineTimer();
    }

    private sealed class InlineTimer : IDispatcherTimer
    {
        public TimeSpan Interval { get; set; }
        public bool IsRepeating { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }

    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _original = CultureInfo.CurrentUICulture;

        public UiCultureScope(string cultureName) =>
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

        public void Dispose() => CultureInfo.CurrentUICulture = _original;
    }
}
