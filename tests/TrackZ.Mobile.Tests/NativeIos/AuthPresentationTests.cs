using System.Text;
using System.Reflection;
using System.Globalization;
using System.Runtime.CompilerServices;
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
using TrackZ.Mobile.Features.Localization;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class AuthPresentationTests
{
    [Fact]
    public void Sign_in_leads_with_the_product_outcome_in_both_languages()
    {
        Assert.Equal("Track training more easily.\nSee clearer results.", AuthTextSet.English.SignInTitle);
        Assert.Equal("ติดตามการฝึกง่ายขึ้น\nเห็นผลลัพธ์ชัดขึ้น", AuthTextSet.Thai.SignInTitle);
        Assert.Equal("Start training with structure.\nSee your progress clearly.", AuthTextSet.English.CreateAccountTitle);
        Assert.Equal("เริ่มต้นฝึกอย่างเป็นระบบ\nเห็นพัฒนาการได้ชัดขึ้น", AuthTextSet.Thai.CreateAccountTitle);
    }

    [Fact]
    public void Fixed_dark_palette_requests_matching_system_chrome()
    {
        using var scope = TestApp.Create();
        var application = scope.App.Services.GetRequiredService<App>();

        Assert.Equal(AppTheme.Dark, application.UserAppTheme);
    }

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
    public async Task Auth_fields_and_actions_keep_accessible_touch_targets()
    {
        using var scope = TestApp.Create();
        var application = scope.App.Services.GetRequiredService<App>();
        _ = application.CreateTestWindow();
        await application.Initialization;

        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var create = scope.App.Services.GetRequiredService<CreateAccountPage>();
        VisualElement[] controls =
        {
            signIn.EmailEntry, signIn.PasswordEntry, signIn.SubmitButton, signIn.SwitchModeButton,
            create.EmailEntry, create.PasswordEntry, create.SubmitButton, create.SwitchModeButton
        };

        Assert.All(controls, control => Assert.True(control.MinimumHeightRequest >= 44));
        Assert.Equal(56, signIn.EmailField.MinimumHeightRequest);
        Assert.Equal(56, signIn.PasswordField.MinimumHeightRequest);
        Assert.Equal(56, signIn.EmailEntry.MinimumHeightRequest);
        Assert.Equal(56, signIn.PasswordEntry.MinimumHeightRequest);
        Assert.Equal(56, create.EmailField.MinimumHeightRequest);
        Assert.Equal(56, create.PasswordField.MinimumHeightRequest);
        Assert.Equal(56, create.EmailEntry.MinimumHeightRequest);
        Assert.Equal(56, create.PasswordEntry.MinimumHeightRequest);
    }

    [Fact]
    public void Sign_in_keeps_brand_and_field_identity_visible_while_the_user_types()
    {
        using var culture = new UiCultureScope("th-TH");
        using var scope = TestApp.Create();
        var signIn = scope.App.Services.GetRequiredService<SignInPage>();

        signIn.EmailEntry.Text = "lift@example.com";
        signIn.PasswordEntry.Text = "Correct-Horse-9";

        var brandMark = signIn.FindByName<Image>("BrandLogo");
        var brandName = signIn.FindByName<Label>("BrandNameLabel");
        var emailLabel = signIn.FindByName<Label>("EmailFieldLabel");
        var passwordLabel = signIn.FindByName<Label>("PasswordFieldLabel");

        Assert.False(Shell.GetNavBarIsVisible(signIn));
        Assert.NotNull(brandMark);
        Assert.Equal("TrackZ", brandName?.Text);
        Assert.Equal(AuthTextSet.Thai.EmailLabel, emailLabel?.Text);
        Assert.Equal(AuthTextSet.Thai.PasswordLabel, passwordLabel?.Text);
        Assert.True(emailLabel?.IsVisible);
        Assert.True(passwordLabel?.IsVisible);
    }

    [Fact]
    public void Sign_in_presents_the_approved_centered_brand_and_single_scrollable_flow()
    {
        using var scope = TestApp.Create();
        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var root = Assert.IsType<Grid>(signIn.Content);
        var content = signIn.FindByName<ScrollView>("AuthScroll");
        Assert.NotNull(content);
        var flow = Assert.IsType<VerticalStackLayout>(content.Content);
        var brand = signIn.FindByName<VerticalStackLayout>("BrandLockup");
        var title = signIn.FindByName<Label>("SignInTitleLabel");
        var form = signIn.FindByName<VerticalStackLayout>("CredentialForm");
        var actions = signIn.FindByName<VerticalStackLayout>("AuthActions");
        var logo = signIn.FindByName<Image>("BrandLogo");

        Assert.NotNull(brand);
        Assert.NotNull(title);
        Assert.NotNull(form);
        Assert.NotNull(actions);
        Assert.NotNull(logo);
        Assert.Equal(LayoutAlignment.Center, brand!.HorizontalOptions.Alignment);
        Assert.Equal(LayoutAlignment.Center, title!.HorizontalOptions.Alignment);
        Assert.Equal(TextAlignment.Center, title.HorizontalTextAlignment);
        Assert.Equal("NotoSansThaiLight", title.FontFamily);
        Assert.Equal(28, title.FontSize);
        Assert.True(flow.Children.IndexOf(brand!) < flow.Children.IndexOf(title!));
        Assert.True(flow.Children.IndexOf(title!) < flow.Children.IndexOf(form!));
        Assert.True(flow.Children.IndexOf(form!) < flow.Children.IndexOf(actions!));
        Assert.Same(actions, signIn.SubmitButton.Parent);
        Assert.Null(signIn.FindByName<Label>("WelcomeBodyLabel"));
    }

    [Fact]
    public void Create_account_reuses_the_centered_auth_pattern_in_one_scrollable_flow()
    {
        using var scope = TestApp.Create();
        var create = scope.App.Services.GetRequiredService<CreateAccountPage>();
        var root = Assert.IsType<Grid>(create.Content);
        var scroll = create.FindByName<ScrollView>("AuthScroll");
        var flow = Assert.IsType<VerticalStackLayout>(scroll?.Content);
        var brand = create.FindByName<VerticalStackLayout>("BrandLockup");
        var title = create.FindByName<Label>("CreateAccountTitleLabel");
        var form = create.FindByName<VerticalStackLayout>("CredentialForm");
        var actions = create.FindByName<VerticalStackLayout>("AuthActions");

        Assert.False(NavigationPage.GetHasNavigationBar(create));
        Assert.NotNull(create.FindByName<ImageButton>("BackAction"));
        Assert.NotNull(create.FindByName<Image>("BrandLogo"));
        Assert.Equal(LayoutAlignment.Center, brand?.HorizontalOptions.Alignment);
        Assert.Equal(TextAlignment.Center, title?.HorizontalTextAlignment);
        Assert.Equal("NotoSansThaiLight", title?.FontFamily);
        Assert.Equal(28, title?.FontSize);
        Assert.True(flow.Children.IndexOf(brand!) < flow.Children.IndexOf(title!));
        Assert.True(flow.Children.IndexOf(title!) < flow.Children.IndexOf(form!));
        Assert.True(flow.Children.IndexOf(form!) < flow.Children.IndexOf(actions!));
        Assert.Same(actions, create.SubmitButton.Parent);
        Assert.Null(create.FindByName<Label>("WelcomeBodyLabel"));
    }

    [Theory]
    [InlineData(375, 667)]
    [InlineData(390, 844)]
    [InlineData(430, 932)]
    [InlineData(844, 390)]
    public async Task Composed_sign_in_keeps_the_complete_flow_available_at_supported_sizes(double width, double height)
    {
        using var scope = TestApp.Create();
        var application = scope.App.Services.GetRequiredService<App>();
        _ = application.CreateTestWindow();
        await application.Initialization;
        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var signRoot = Assert.IsType<Grid>(signIn.Content);
        var scroll = signIn.FindByName<ScrollView>("AuthScroll");
        Assert.NotNull(scroll);
        var flow = Assert.IsType<VerticalStackLayout>(scroll.Content);
        var brand = signIn.FindByName<VerticalStackLayout>("BrandLockup");
        var form = signIn.FindByName<VerticalStackLayout>("CredentialForm");

        Assert.Equal(0, Grid.GetRow(scroll));

        Arrange(signRoot, width, height);

        Assert.NotNull(brand);
        Assert.NotNull(form);
        Assert.True(scroll.Height > 0);
        Assert.True(flow.Height > 0);
        Assert.True(signIn.EmailField.Height > 0);
        Assert.True(signIn.PasswordField.Height > 0);
        Assert.True(flow.Children.IndexOf(brand!) < flow.Children.IndexOf(form!));
        Assert.True(AbsoluteY(signIn.EmailField, flow) < AbsoluteY(signIn.PasswordField, flow));
        Assert.True(AbsoluteY(signIn.SubmitButton, flow) > AbsoluteY(signIn.PasswordField, flow));
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
    public async Task Popped_create_account_page_and_transient_form_are_released()
    {
        using var scope = TestApp.Create();
        var signIn = scope.App.Services.GetRequiredService<SignInPage>();
        var navigation = new NavigationPage(signIn);

        var references = await PushAndPopCreateAccountAsync(navigation, scope.App.Services);
        await WaitForCollectionAsync(references.Page, references.Form);

        Assert.False(references.Page.TryGetTarget(out _));
        Assert.False(references.Form.TryGetTarget(out _));
        GC.KeepAlive(navigation);
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
        Assert.Equal("Email", signIn.FindByName<Label>("EmailFieldLabel").Text);
        Assert.Equal("Password", signIn.FindByName<Label>("PasswordFieldLabel").Text);
        Assert.Null(signIn.EmailEntry.Placeholder);
        Assert.Null(signIn.PasswordEntry.Placeholder);
        Assert.Equal("Email", create.FindByName<Label>("EmailFieldLabel").Text);
        Assert.Equal("Password", create.FindByName<Label>("PasswordFieldLabel").Text);
        Assert.Null(create.EmailEntry.Placeholder);
        Assert.Null(create.PasswordEntry.Placeholder);
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
        Assert.Equal(AuthTextSet.Thai.EmailLabel, signIn.FindByName<Label>("EmailFieldLabel").Text);
        Assert.Equal(AuthTextSet.Thai.PasswordLabel, signIn.FindByName<Label>("PasswordFieldLabel").Text);
        Assert.Null(signIn.EmailEntry.Placeholder);
        Assert.Null(signIn.PasswordEntry.Placeholder);
        Assert.Equal(AuthTextSet.Thai.EmailLabel, create.FindByName<Label>("EmailFieldLabel").Text);
        Assert.Equal(AuthTextSet.Thai.PasswordLabel, create.FindByName<Label>("PasswordFieldLabel").Text);
        Assert.Null(create.EmailEntry.Placeholder);
        Assert.Null(create.PasswordEntry.Placeholder);
        Assert.Equal(AuthTextSet.Thai.PasswordAccessibilityLabel, SemanticProperties.GetDescription(create.PasswordEntry));
        Assert.Equal(AuthTextSet.Thai.CheckingSessionAccessibilityLabel, SemanticProperties.GetDescription(gate.StatusIndicator));
    }

    private static string CreateToken(DateTimeOffset expiresAt)
    {
        static string Part(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("{\"alg\":\"none\"}")}.{Part($"{{\"sub\":\"99999999-9999-9999-9999-999999999999\",\"sid\":\"aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb\",\"exp\":{expiresAt.ToUnixTimeSeconds()}}}")}.signature";
    }

    private static void Arrange(VisualElement element, double width, double height)
    {
        AttachHeadlessLayoutHandlers(element);
        element.Measure(width, height);
        element.Arrange(new Rect(0, 0, width, height));
    }

    private static void AttachHeadlessLayoutHandlers(VisualElement element)
    {
        element.Handler = new HeadlessLayoutHandler(element);
        if (element is not IVisualTreeElement tree) return;
        foreach (var child in tree.GetVisualChildren().OfType<VisualElement>())
            AttachHeadlessLayoutHandlers(child);
    }

    private static double AbsoluteY(VisualElement element, VisualElement root)
    {
        var y = element.Y;
        for (var parent = element.Parent as VisualElement; parent is not null && parent != root; parent = parent.Parent as VisualElement)
            y += parent.Y;
        return y;
    }

    private sealed class HeadlessLayoutHandler(VisualElement view) : IViewHandler
    {
        public bool HasContainer { get; set; }
        public object? ContainerView => null;
        public object? PlatformView => null;
        public IView VirtualView { get; private set; } = view;
        IElement IElementHandler.VirtualView => VirtualView;
        public IMauiContext? MauiContext { get; private set; }

        public Size GetDesiredSize(double widthConstraint, double heightConstraint)
        {
            if (VirtualView is ICrossPlatformLayout layout)
                return layout.CrossPlatformMeasure(widthConstraint, heightConstraint);
            var visual = (VisualElement)VirtualView;
            var width = visual.WidthRequest >= 0 ? visual.WidthRequest : Math.Max(0, visual.MinimumWidthRequest);
            var height = visual.HeightRequest >= 0 ? visual.HeightRequest : Math.Max(0, visual.MinimumHeightRequest);
            return new Size(Math.Min(width, widthConstraint), Math.Min(height, heightConstraint));
        }

        public void PlatformArrange(Rect frame)
        {
            ((VisualElement)VirtualView).Frame = frame;
            if (VirtualView is ICrossPlatformLayout layout)
                layout.CrossPlatformArrange(new Rect(0, 0, frame.Width, frame.Height));
        }

        public void SetMauiContext(IMauiContext mauiContext) => MauiContext = mauiContext;
        public void SetVirtualView(IElement view) => VirtualView = (IView)view;
        public void UpdateValue(string property) { }
        public void Invoke(string command, object? args = null) { }
        public void DisconnectHandler() { }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference<CreateAccountPage> Page, WeakReference<AuthFormViewModel> Form)> PushAndPopCreateAccountAsync(
        NavigationPage navigation,
        IServiceProvider services)
    {
        var page = services.GetRequiredService<CreateAccountPage>();
        var pageReference = new WeakReference<CreateAccountPage>(page);
        var formReference = new WeakReference<AuthFormViewModel>(page.Form);
        await navigation.PushAsync(page, animated: false);
        await navigation.PopAsync(animated: false);
        return (pageReference, formReference);
    }

    private static async Task WaitForCollectionAsync(params object[] references)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (references.All(reference => reference switch
                {
                    WeakReference<CreateAccountPage> page => !page.TryGetTarget(out _),
                    WeakReference<AuthFormViewModel> form => !form.TryGetTarget(out _),
                    _ => false
                }))
                return;
            await Task.Yield();
        }
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
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th"
                ? AppLanguage.Thai
                : AppLanguage.English;
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
            }, new FixedLanguageStore(language));
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

    private sealed class FixedLanguageStore(AppLanguage language) : IAppLanguageStore
    {
        public AppLanguage Read() => language;
        public void Write(AppLanguage value) => language = value;
    }
}
