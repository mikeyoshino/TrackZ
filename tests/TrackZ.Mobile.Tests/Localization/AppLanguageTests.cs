using System.Globalization;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.DependencyInjection;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Localization;
using TrackZ.Mobile.Features.Workout;
using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.Localization;

public sealed class AppLanguageTests
{
    public static TheoryData<string?, AppLanguage> StoredLanguageCases => new()
    {
        { null, AppLanguage.Thai },
        { string.Empty, AppLanguage.Thai },
        { "ja-JP", AppLanguage.Thai },
        { "TH-th", AppLanguage.Thai },
        { "th-TH", AppLanguage.Thai },
        { "en-US", AppLanguage.English }
    };

    [Theory]
    [MemberData(nameof(StoredLanguageCases))]
    public void Store_reads_only_the_two_stable_language_tags(string? stored, AppLanguage expected)
    {
        var preferences = new MemoryPreferences();
        if (stored is not null) preferences.Set(MauiAppLanguageStore.PreferenceKey, stored);

        Assert.Equal(expected, new MauiAppLanguageStore(preferences).Read());
    }

    [Theory]
    [InlineData(AppLanguage.Thai, "th-TH")]
    [InlineData(AppLanguage.English, "en-US")]
    public void Store_writes_the_stable_culture_tag(AppLanguage language, string expectedTag)
    {
        var preferences = new MemoryPreferences();

        new MauiAppLanguageStore(preferences).Write(language);

        Assert.Equal(expectedTag, preferences.Get<string?>(MauiAppLanguageStore.PreferenceKey, null));
    }

    [Fact]
    public void Applying_Thai_updates_current_and_default_thread_cultures()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            AppLanguageCulture.Apply(AppLanguage.Thai);

            Assert.Equal("th-TH", CultureInfo.CurrentCulture.Name);
            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentCulture!.Name);
            Assert.Equal("th-TH", CultureInfo.DefaultThreadCurrentUICulture!.Name);
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Theory]
    [InlineData("th-TH", "ภาษา", "ไทย", "English")]
    [InlineData("en-US", "Language", "Thai", "English")]
    public void Language_resources_use_exact_requested_culture(
        string cultureName,
        string language,
        string thai,
        string english)
    {
        var text = MobileResources.ForCulture(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(language, text.Language);
        Assert.Equal(thai, text.ThaiLanguage);
        Assert.Equal(english, text.EnglishLanguage);
        Assert.False(string.IsNullOrWhiteSpace(text.LanguageSwitchFailed));
    }

    [Fact]
    public void Maui_composition_defaults_to_Thai_before_localized_services_are_resolved()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            var store = new MemoryLanguageStore();
            using var app = MauiProgram.CreateMauiApp(appLanguageStore: store);

            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Same(store, app.Services.GetService(typeof(IAppLanguageStore)));
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Fact]
    public void Portable_composition_defaults_to_Thai_without_calling_a_platform_preference_backend()
    {
        var snapshot = CultureSnapshot.Capture();
        try
        {
            using var app = MauiProgram.CreateMauiApp();

            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Equal(AppLanguage.Thai,
                app.Services.GetRequiredService<IAppLanguageStore>().Read());
        }
        finally
        {
            snapshot.Restore();
        }
    }

    [Fact]
    public void Mobile_resource_files_have_identical_non_empty_keys()
    {
        var resources = Path.Combine(
            FindSolutionDirectory(),
            "src",
            "TrackZ.Mobile.Core",
            "Resources");
        var english = ReadResourceValues(Path.Combine(resources, "MobileStrings.resx"));
        var thai = ReadResourceValues(Path.Combine(resources, "MobileStrings.th.resx"));

        Assert.Equal(english.Keys.Order(), thai.Keys.Order());
        Assert.All(english, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.All(thai, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    private static Dictionary<string, string> ReadResourceValues(string path) =>
        XDocument.Load(path)
            .Descendants("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("TrackZ.slnx was not found.");
    }

    private sealed class MemoryLanguageStore : IAppLanguageStore
    {
        public AppLanguage Read() => AppLanguage.Thai;
        public void Write(AppLanguage language) { }
    }

    private sealed class MemoryPreferences : IPreferences
    {
        private readonly Dictionary<string, object?> _values = [];

        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => _values.Remove(key);
        public void Clear(string? sharedName = null) => _values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => _values[key] = value;
        public T Get<T>(string key, T defaultValue, string? sharedName = null) =>
            _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
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

public sealed class AppLanguageChangerTests
{
    [Fact]
    public async Task Same_language_is_a_no_op()
    {
        using var host = new RecordingUiHost();
        var store = new MutableLanguageStore(AppLanguage.Thai);
        var sut = new MauiAppLanguageChanger(store, host, CreateAuthentication());

        await sut.ChangeAsync(AppLanguage.Thai);

        Assert.Empty(host.RootTabRoutes);
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task Concurrent_same_target_switches_install_one_replacement()
    {
        var culture = CultureSnapshot.Capture();
        using var host = new RecordingUiHost();
        try
        {
            var store = new MutableLanguageStore(AppLanguage.Thai);
            var sut = new MauiAppLanguageChanger(store, host, CreateAuthentication());

            await Task.WhenAll(
                sut.ChangeAsync(AppLanguage.English),
                sut.ChangeAsync(AppLanguage.English));

            Assert.Equal(AppLanguage.English, sut.Current);
            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
            Assert.Equal(1, host.InstallCount);
            Assert.Equal("train", Assert.Single(host.RootTabRoutes));
        }
        finally
        {
            culture.Restore();
        }
    }

    [Fact]
    public async Task Failed_install_restores_preference_and_culture_and_disposes_candidate()
    {
        var culture = CultureSnapshot.Capture();
        AppLanguageCulture.Apply(AppLanguage.Thai);
        using var host = new RecordingUiHost { FailInstall = true };
        try
        {
            var store = new MutableLanguageStore(AppLanguage.Thai);
            var sut = new MauiAppLanguageChanger(store, host, CreateAuthentication());

            await Assert.ThrowsAsync<AppLanguageChangeException>(() =>
                sut.ChangeAsync(AppLanguage.English));

            Assert.Equal(AppLanguage.Thai, store.Read());
            Assert.Equal(AppLanguage.Thai, sut.Current);
            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.True(host.LastProbe?.Disposed);
        }
        finally
        {
            culture.Restore();
        }
    }

    [Fact]
    public async Task Preference_failure_is_sanitized_and_keeps_the_old_language()
    {
        var culture = CultureSnapshot.Capture();
        AppLanguageCulture.Apply(AppLanguage.Thai);
        using var host = new RecordingUiHost();
        try
        {
            var store = new MutableLanguageStore(AppLanguage.Thai) { FailedValue = AppLanguage.English };
            var sut = new MauiAppLanguageChanger(store, host, CreateAuthentication());

            var error = await Assert.ThrowsAsync<AppLanguageChangeException>(() =>
                sut.ChangeAsync(AppLanguage.English));

            Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(AppLanguage.Thai, store.Read());
            Assert.Equal(AppLanguage.Thai, sut.Current);
            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.Equal(0, host.InstallCount);
        }
        finally
        {
            culture.Restore();
        }
    }

    [Fact]
    public async Task Caller_cancellation_during_install_rolls_back_and_rethrows()
    {
        var culture = CultureSnapshot.Capture();
        AppLanguageCulture.Apply(AppLanguage.Thai);
        using var cancellation = new CancellationTokenSource();
        using var host = new RecordingUiHost { BeforeInstall = cancellation.Cancel };
        try
        {
            var store = new MutableLanguageStore(AppLanguage.Thai);
            var sut = new MauiAppLanguageChanger(store, host, CreateAuthentication());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                sut.ChangeAsync(AppLanguage.English, cancellation.Token));

            Assert.Equal(AppLanguage.Thai, store.Read());
            Assert.Equal(AppLanguage.Thai, sut.Current);
            Assert.Equal("th-TH", CultureInfo.CurrentUICulture.Name);
            Assert.True(host.LastProbe?.Disposed);
        }
        finally
        {
            culture.Restore();
        }
    }

    [Fact]
    public async Task Auth_change_during_language_switch_discards_stale_root_and_installs_current_state()
    {
        var culture = CultureSnapshot.Capture();
        var authentication = CreateAuthentication();
        using var host = new RecordingUiHost
        {
            BeforePrepare = (_, attempt) =>
            {
                if (attempt == 1)
                    authentication.RequireSignInAsync().GetAwaiter().GetResult();
            }
        };
        try
        {
            var sut = new MauiAppLanguageChanger(
                new MutableLanguageStore(AppLanguage.Thai),
                host,
                authentication);

            await sut.ChangeAsync(AppLanguage.English);

            Assert.Equal(
                [AuthGateState.CheckingSession, AuthGateState.SignedOut],
                host.PreparedSnapshots.Select(snapshot => snapshot.State));
            Assert.True(host.Probes[0].Disposed);
            Assert.False(host.Probes[1].Disposed);
            Assert.Equal(1, host.InstallCount);
        }
        finally
        {
            culture.Restore();
        }
    }

    private static AuthGateCoordinator CreateAuthentication() => new(
        new MobileTokenStore(new MemoryTokenStorage()),
        new NoopIdentitySession(),
        new NoopPrivateDataCleaner(),
        new AccountSessionBoundary(),
        new FixedDeviceName(),
        new OnlineConnectivity(),
        TimeProvider.System);

    private sealed class MutableLanguageStore(AppLanguage language) : IAppLanguageStore
    {
        public AppLanguage? FailedValue { get; init; }
        public int WriteCount { get; private set; }
        public AppLanguage Read() => language;
        public void Write(AppLanguage value)
        {
            WriteCount++;
            if (value == FailedValue) throw new InvalidOperationException("secret preference failure");
            language = value;
        }
    }

    private sealed class RecordingUiHost : ILocalizedUiHost, IDisposable
    {
        private readonly List<ServiceProvider> _providers = [];
        private LocalizedUiInstallation? _active;

        public bool FailInstall { get; init; }
        public Action? BeforeInstall { get; init; }
        public Action<AuthGateSnapshot, int>? BeforePrepare { get; init; }
        public int InstallCount { get; private set; }
        public List<string?> RootTabRoutes { get; } = [];
        public List<AuthGateSnapshot> PreparedSnapshots { get; } = [];
        public List<DisposeProbe> Probes { get; } = [];
        public DisposeProbe? LastProbe { get; private set; }
        public string? CurrentRootTabRoute => "train";

        public LocalizedUiInstallation Prepare(AuthGateSnapshot snapshot)
        {
            PreparedSnapshots.Add(snapshot);
            var provider = new ServiceCollection()
                .AddScoped<DisposeProbe>()
                .BuildServiceProvider();
            _providers.Add(provider);
            var scope = new LocalizedUiScope(provider.CreateScope());
            LastProbe = scope.Services.GetRequiredService<DisposeProbe>();
            Probes.Add(LastProbe);
            BeforePrepare?.Invoke(snapshot, PreparedSnapshots.Count);
            return new LocalizedUiInstallation(scope, new ContentPage());
        }

        public Task InstallAsync(
            LocalizedUiInstallation installation,
            string? rootTabRoute,
            CancellationToken cancellationToken)
        {
            BeforeInstall?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailInstall) throw new InvalidOperationException("secret platform failure");
            InstallCount++;
            RootTabRoutes.Add(rootTabRoute);
            _active?.Dispose();
            _active = installation;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _active?.Dispose();
            foreach (var provider in _providers) provider.Dispose();
        }
    }

    private sealed class DisposeProbe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
        public string DeviceName => "tests";
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
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

public sealed class ProfileLanguageSelectionTests
{
    [Fact]
    public async Task Profile_exposes_Thai_then_English_and_updates_selection_after_switch()
    {
        var language = new RecordingLanguageChanger(AppLanguage.Thai);
        var sut = new ProfileViewModel(
            new EmptyProgressSource(),
            new OnlineConnectivity(),
            GamificationResources.Thai,
            language,
            MobileResources.ForCulture(CultureInfo.GetCultureInfo("th-TH")));

        Assert.Collection(
            sut.Languages,
            option =>
            {
                Assert.Equal(AppLanguage.Thai, option.Value);
                Assert.Equal("ไทย", option.Label);
                Assert.True(option.IsSelected);
            },
            option =>
            {
                Assert.Equal(AppLanguage.English, option.Value);
                Assert.Equal("English", option.Label);
                Assert.False(option.IsSelected);
            });

        await sut.ChangeLanguageCommand.ExecuteAsync(AppLanguage.English);

        Assert.Equal([AppLanguage.English], language.Requests);
        Assert.False(sut.Languages[0].IsSelected);
        Assert.True(sut.Languages[1].IsSelected);
        Assert.Null(sut.LanguageError);
    }

    [Fact]
    public async Task Profile_maps_sanitized_switch_failure_to_localized_copy()
    {
        var language = new RecordingLanguageChanger(AppLanguage.Thai) { Fail = true };
        var mobileText = MobileResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));
        var sut = new ProfileViewModel(
            new EmptyProgressSource(),
            new OnlineConnectivity(),
            GamificationResources.Thai,
            language,
            mobileText);

        await sut.ChangeLanguageCommand.ExecuteAsync(AppLanguage.English);

        Assert.Equal(mobileText.LanguageSwitchFailed, sut.LanguageError);
        Assert.True(sut.Languages[0].IsSelected);
        Assert.False(sut.Languages[1].IsSelected);
    }

    private sealed class RecordingLanguageChanger(AppLanguage current) : IAppLanguageChanger
    {
        public List<AppLanguage> Requests { get; } = [];
        public bool Fail { get; init; }
        public AppLanguage Current { get; private set; } = current;
        public bool IsChanging => false;

        public Task ChangeAsync(AppLanguage language, CancellationToken cancellationToken = default)
        {
            Requests.Add(language);
            if (Fail) throw new AppLanguageChangeException();
            Current = language;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(null);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }
}
