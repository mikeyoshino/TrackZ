using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class MomentumHomePresentationTests
{
    [Fact]
    public void Momentum_home_copy_is_localized_and_reuses_existing_action_contracts()
    {
        var english = WorkoutResources.English;
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("Start workout", english.StartWorkout);
        Assert.Equal("Continue workout", english.ContinueWorkout);
        Assert.Equal("Try again", english.TryAgain);
        Assert.Equal("Could not repeat that workout. Try again.", english.HomeRepeatFailed);
        Assert.Equal("Workout saved. Could not open it. Tap Continue.", english.HomeOpenWorkoutFailed);
        Assert.Equal("Couldn't open progress. Try again.", english.HomeOpenProgressFailed);
        Assert.Equal("เริ่มออกกำลังกาย", thai.StartWorkout);
        Assert.Equal("ออกกำลังกายต่อ", thai.ContinueWorkout);
        Assert.Equal("ลองอีกครั้ง", thai.TryAgain);
        Assert.Equal("เริ่มการฝึกแบบเดิมไม่สำเร็จ ลองอีกครั้ง", thai.HomeRepeatFailed);
        Assert.Equal("บันทึกการฝึกแล้ว แต่เปิดไม่สำเร็จ แตะออกกำลังกายต่อ", thai.HomeOpenWorkoutFailed);
        Assert.Equal("เปิดข้อมูลผลงานไม่สำเร็จ ลองอีกครั้ง", thai.HomeOpenProgressFailed);

        var englishHome = TrackZ.Mobile.Features.Train.HomeCopy.ForCulture(CultureInfo.GetCultureInfo("en-US"));
        var thaiHome = TrackZ.Mobile.Features.Train.HomeCopy.ForCulture(CultureInfo.GetCultureInfo("th-TH"));
        Assert.Equal("Today", englishHome.Today);
        Assert.Equal("Ready to train?", englishHome.ReadyHeadline);
        Assert.Equal("Today's workout", englishHome.TodaysWorkout);
        Assert.Equal("View summary ›", englishHome.ViewSummary);
        Assert.Equal("View all ›", englishHome.ViewAll);
        Assert.Equal("วันนี้", thaiHome.Today);
        Assert.Equal("พร้อมฝึกกันไหม?", thaiHome.ReadyHeadline);
        Assert.Equal("การฝึกวันนี้", thaiHome.TodaysWorkout);
        Assert.Equal("ดูสรุป ›", thaiHome.ViewSummary);
        Assert.Equal("ดูทั้งหมด ›", thaiHome.ViewAll);
        Assert.Equal("ฝึกครั้งล่าสุด", thaiHome.LatestWorkoutHeading);
        var latest = Repeat() with { BodyParts = [BodyPart.Chest, BodyPart.Back] };
        var nextDay = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("Chest · Back", englishHome.FormatLatestWorkoutTitle(latest));
        Assert.Equal("อก · หลัง", thaiHome.FormatLatestWorkoutTitle(latest));
        Assert.Equal("Yesterday · 6 exercises · 18 sets", englishHome.FormatLatestWorkoutMeta(latest, nextDay));
        Assert.Equal("เมื่อวาน · 6 ท่า · 18 เซ็ต", thaiHome.FormatLatestWorkoutMeta(latest, nextDay));
        Assert.All(
            typeof(TrackZ.Mobile.Features.Train.HomeCopy).GetProperties()
                .Where(property => property.PropertyType == typeof(string)),
            property =>
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(englishHome)));
                Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(thaiHome)));
            });
    }

    [Fact]
    public async Task Ready_home_inflates_the_approved_action_first_hierarchy_with_one_primary_action()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, Repeat()),
            Progress());
        var page = context.Page;
        var viewModel = context.ViewModel;
        var primary = Assert.IsType<Button>(page.FindByName("HeroActionButton"));
        var viewSummary = Assert.IsType<Button>(page.FindByName("ViewSummaryAction"));
        var viewHistory = Assert.IsType<Button>(page.FindByName("ViewHistoryAction"));
        var contextLabel = Assert.IsType<Label>(page.FindByName("HomeContextLabel"));
        var headline = Assert.IsType<Label>(page.FindByName("HomeHeadlineLabel"));
        var header = Assert.IsType<Grid>(page.FindByName("HomeHeader"));
        var brand = Assert.IsType<Image>(page.FindByName("HomeBrandLogo"));

        Assert.Equal(page.Copy.Today, contextLabel.Text);
        Assert.Equal(page.Copy.ReadyHeadline, headline.Text);
        Assert.Equal("trackz_auth_logo.png", Assert.IsType<FileImageSource>(brand.Source).File);
        Assert.InRange(brand.WidthRequest, 28, 36);
        Assert.InRange(brand.HeightRequest, 28, 36);
        Assert.Same(header, brand.Parent);
        Assert.Equal(1, Grid.GetColumn(brand));
        Assert.DoesNotContain(Descendants(header).OfType<Label>(), label => label.Text == "TrackZ");
        Assert.Equal(page.Copy.TodaysWorkout, Assert.IsType<Label>(page.FindByName("HeroTitleLabel")).Text);
        Assert.Equal(page.Copy.ChooseWorkoutSupporting, Assert.IsType<Label>(page.FindByName("HeroSupportingLabel")).Text);
        Assert.True(primary.MinimumHeightRequest >= 44);
        Assert.Same(viewModel.HeroActionCommand, primary.Command);
        Assert.True(viewSummary.MinimumHeightRequest >= 44);
        Assert.True(viewHistory.MinimumHeightRequest >= 44);
        Assert.Equal(page.Copy.ViewSummary, viewSummary.Text);
        Assert.Equal(page.Copy.ViewSummary, SemanticProperties.GetDescription(viewSummary));
        Assert.Equal(page.Copy.ViewAll, viewHistory.Text);
        Assert.Equal(page.Copy.ViewAll, SemanticProperties.GetDescription(viewHistory));
        Assert.Single(
            Descendants(page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
        Assert.Contains(primary, Descendants(Assert.IsType<Border>(page.FindByName("HomeHero"))));
        var root = Assert.IsType<VerticalStackLayout>(
            Assert.IsType<ScrollView>(page.FindByName("MomentumHomeScroll")).Content);
        var hero = Assert.IsAssignableFrom<IView>(page.FindByName("HomeHero"));
        var week = Assert.IsAssignableFrom<IView>(page.FindByName("CoachWeekSection"));
        var advice = Assert.IsAssignableFrom<IView>(page.FindByName("CoachHomeHost"));
        var latest = Assert.IsAssignableFrom<IView>(page.FindByName("LatestWorkoutSection"));
        var children = root.Children.ToList();

        Assert.True(children.IndexOf(hero) < children.IndexOf(week));
        Assert.True(children.IndexOf(week) < children.IndexOf(advice));
        Assert.True(children.IndexOf(advice) < children.IndexOf(latest));
        Assert.Null(page.FindByName("MotivationStrip"));
        Assert.Null(page.FindByName("LevelMetric"));
        Assert.False(Assert.IsType<VerticalStackLayout>(week).IsVisible);
        Assert.False(Assert.IsType<VerticalStackLayout>(advice).IsVisible);
        Assert.True(Assert.IsType<VerticalStackLayout>(latest).IsVisible);
        Assert.Null(page.FindByName("TrainAgainCard"));
        Assert.Null(page.FindByName("WeeklyGoalCard"));
        Assert.Null(page.FindByName("LatestPerformanceCard"));

        var latestCard = Assert.IsType<Border>(page.FindByName("LatestWorkoutCard"));
        var labels = Descendants(latestCard).OfType<Label>().Select(label => label.Text).ToArray();
        Assert.Contains(page.LatestWorkoutTitle, labels);
        Assert.Contains(page.LatestWorkoutMeta, labels);
        var artwork = Assert.Single(Descendants(latestCard).OfType<Border>());
        var images = Descendants(artwork).OfType<Image>().ToArray();
        Assert.Equal("exercise_placeholder.png", Assert.IsType<FileImageSource>(images[0].Source).File);
        Assert.Equal(viewModel.RepeatWorkout?.ThumbnailPath, images[1].Source?.ToString());
        Assert.True(AutomationProperties.GetExcludedWithChildren(artwork));
    }

    [Fact]
    public async Task Active_reload_keeps_the_same_primary_button_and_latest_history()
    {
        var source = new QueueDashboardSource(
            new TrainDashboardSnapshot(null, Repeat()),
            new TrainDashboardSnapshot(Active(), Repeat()));
        await using var context = await TestHome.CreateAsync(source, Progress());
        var primary = Assert.IsType<Button>(context.Page.FindByName("HeroActionButton"));

        await context.ViewModel.LoadAsync();

        Assert.Same(primary, context.Page.FindByName("HeroActionButton"));
        Assert.Equal("Continue workout", primary.Text);
        Assert.Equal(context.ViewModel.HomeHeadlineText,
            Assert.IsType<Label>(context.Page.FindByName("HomeHeadlineLabel")).Text);
        Assert.True(Assert.IsType<VerticalStackLayout>(context.Page.FindByName("LatestWorkoutSection")).IsVisible);
        Assert.Single(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
    }

    [Fact]
    public async Task Missing_progress_does_not_hide_real_latest_workout_or_show_empty_coach_hosts()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, Repeat()),
            cachedProgress: null);

        Assert.False(Assert.IsType<VerticalStackLayout>(context.Page.FindByName("CoachWeekSection")).IsVisible);
        Assert.False(Assert.IsType<VerticalStackLayout>(context.Page.FindByName("CoachHomeHost")).IsVisible);
        Assert.True(Assert.IsType<VerticalStackLayout>(context.Page.FindByName("LatestWorkoutSection")).IsVisible);
    }

    [Fact]
    public async Task Empty_history_hides_the_entire_latest_workout_section()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, null),
            Progress());

        Assert.False(Assert.IsType<VerticalStackLayout>(context.Page.FindByName("LatestWorkoutSection")).IsVisible);
        Assert.NotNull(context.Page.FindByName("LatestWorkoutCard"));
    }

    [Fact]
    public async Task Coach_hosts_show_only_supplied_real_content_and_clear_without_placeholders()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, null),
            EmptyProgress());
        var page = context.Page;

        var week = new Label { Text = "Real week" };
        var advice = new Label { Text = "Real advice" };
        page.SetCoachContent(week, advice);

        var weekHost = Assert.IsType<VerticalStackLayout>(page.FindByName("CoachWeekHost"));
        var adviceHost = Assert.IsType<VerticalStackLayout>(page.FindByName("CoachHomeHost"));
        Assert.True(Assert.IsType<VerticalStackLayout>(page.FindByName("CoachWeekSection")).IsVisible);
        Assert.True(adviceHost.IsVisible);
        Assert.Same(week, Assert.Single(weekHost.Children));
        Assert.Same(advice, Assert.Single(adviceHost.Children));

        page.SetCoachContent(null, null);

        Assert.False(Assert.IsType<VerticalStackLayout>(page.FindByName("CoachWeekSection")).IsVisible);
        Assert.False(adviceHost.IsVisible);
        Assert.Empty(weekHost.Children);
        Assert.Empty(adviceHost.Children);
    }

    [Fact]
    public async Task Unit_change_does_not_replace_actual_latest_workout_metadata_with_performance_mock_data()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, Repeat()),
            Progress());
        var card = Assert.IsType<Border>(context.Page.FindByName("LatestWorkoutCard"));
        var before = Descendants(card).OfType<Label>().Select(label => label.Text).ToArray();

        context.WeightPreference.Set(WeightDisplayUnit.Pounds);

        Assert.Equal(before, Descendants(card).OfType<Label>().Select(label => label.Text));
        Assert.Contains(context.Page.LatestWorkoutTitle, before);
        Assert.Contains(context.Page.LatestWorkoutMeta, before);
        Assert.Same(card, context.Page.FindByName("LatestWorkoutCard"));
    }

    [Fact]
    public async Task Local_failure_shows_one_non_primary_native_retry_action_with_localized_semantics()
    {
        await using var context = await TestHome.CreateAsync(
            new ThrowingDashboardSource(),
            cachedProgress: null);
        var retry = Assert.IsType<Button>(context.Page.FindByName("HomeRetryButton"));

        Assert.True(context.ViewModel.HasError);
        Assert.True(context.ViewModel.HasLoadRetry);
        Assert.True(retry.IsVisible);
        Assert.True(retry.MinimumHeightRequest >= 44);
        Assert.Same(context.ViewModel.RetryCommand, retry.Command);
        Assert.Equal(context.ViewModel.Text.TryAgain, retry.Text);
        Assert.Equal(context.ViewModel.Text.TryAgain, SemanticProperties.GetDescription(retry));
        Assert.Same(context.Application.Resources["TrackZQuietButtonStyle"], retry.Style);
        Assert.Single(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
    }

    [Fact]
    public async Task Latest_history_row_never_exposes_the_train_again_command()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, Repeat()),
            cachedProgress: null);
        var latest = Assert.IsType<Border>(context.Page.FindByName("LatestWorkoutCard"));

        Assert.Empty(latest.GestureRecognizers);
        Assert.DoesNotContain(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Command, context.ViewModel.TrainAgainCommand));
        Assert.NotNull(context.Page.FindByName("ViewHistoryAction"));
        Assert.Single(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
    }

    private static ActiveWorkoutCard Active() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        At(9),
        [BodyPart.Chest, BodyPart.Arms],
        5,
        3,
        8);

    private static RepeatWorkoutShortcut Repeat() => new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        [BodyPart.Chest, BodyPart.Arms],
        At(8),
        6,
        18,
        null,
        [new WorkoutExerciseSelection(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            TrackingMode.Weighted)]);

    private static RepeatWorkoutShortcut InvalidRepeat() =>
        Repeat() with
        {
            Selections = [new WorkoutExerciseSelection(Guid.Empty, TrackingMode.Weighted)]
        };

    private static ProgressSnapshot Progress() => new(
        new ProgressSummaryDto(
            1000m,
            500m,
            3,
            1,
            [new ExerciseProgressSummaryDto(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "Bench Press",
                TrackingMode.Weighted,
                At(10),
                70.125m,
                null,
                8,
                72.5m,
                null,
                6)]),
        new GamificationProfileDto(640, 8, 600, 800, 4, 3, 4, 4, [], []),
        At(11));

    private static ProgressSnapshot EmptyProgress() => new(
        new ProgressSummaryDto(0m, 0m, 0, 0, []),
        new GamificationProfileDto(0, 1, 0, 100, 3, 0, 0, 0, [], []),
        At(11));

    private static DateTimeOffset At(int hour) =>
        new(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);

    private static IEnumerable<Element> Descendants(IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren().OfType<Element>())
        {
            yield return child;
            if (child is IVisualTreeElement tree)
                foreach (var descendant in Descendants(tree))
                    yield return descendant;
        }
    }

    private sealed class TestHome : IAsyncDisposable
    {
        private readonly IDispatcherProvider _originalDispatcher;
        private readonly string _root;

        private TestHome(
            MauiApp app,
            App application,
            TrainPage page,
            TrainTodayViewModel viewModel,
            MutableWeightPreference weightPreference,
            IDispatcherProvider originalDispatcher,
            string root)
        {
            App = app;
            Application = application;
            Page = page;
            ViewModel = viewModel;
            WeightPreference = weightPreference;
            _originalDispatcher = originalDispatcher;
            _root = root;
        }

        public MauiApp App { get; }
        public App Application { get; }
        public TrainPage Page { get; }
        public TrainTodayViewModel ViewModel { get; }
        public MutableWeightPreference WeightPreference { get; }

        public static Task<TestHome> CreateAsync(
            TrainDashboardSnapshot snapshot,
            ProgressSnapshot? cachedProgress) =>
            CreateAsync(new QueueDashboardSource(snapshot), cachedProgress);

        public static async Task<TestHome> CreateAsync(
            ITrainDashboardSource source,
            ProgressSnapshot? cachedProgress)
        {
            var originalDispatcher = DispatcherProvider.Current;
            var root = Path.Combine(Path.GetTempPath(), $"trackz-momentum-home-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
            try
            {
                var weightPreference = new MutableWeightPreference();
                var boundary = new AccountSessionBoundary();
                var database = new TrackZLocalDatabase(Path.Combine(root, "workouts.db"));
                var viewModel = new TrainTodayViewModel(
                    source,
                    boundary,
                    WorkoutResources.English,
                    new CachedProgressSource(cachedProgress),
                    new OfflineConnectivity(),
                    weightPreference,
                    GamificationResources.English,
                    activeWorkouts: new ActiveWorkoutCoordinator(
                        new LocalWorkoutRepository(database),
                        boundary,
                        new FixedClock(At(12))),
                    navigator: new NoOpTrainNavigator(),
                    clock: new FixedClock(At(12)),
                    localTimeZone: TimeZoneInfo.Utc);
                var app = MauiProgram.CreateMauiApp(services =>
                {
                    services.AddSingleton(new ExerciseCache(Path.Combine(root, "exercises.db")));
                    services.AddSingleton(new ExerciseHistoryCache(Path.Combine(root, "history.db")));
                    services.AddSingleton(database);
                    services.AddSingleton(new ProgressSnapshotCache(Path.Combine(root, "progress.json")));
                    services.AddSingleton<IExerciseThumbnailCache, NullThumbnailCache>();
                    services.AddSingleton<IConnectivityService, OfflineConnectivity>();
                    services.AddSingleton<IWorkoutPreferenceStore, MemoryPreferences>();
                    services.AddSingleton(viewModel);
                });
                var application = app.Services.GetRequiredService<App>();
                var page = app.Services.GetRequiredService<TrainPage>();
                await viewModel.LoadAsync();
                return new TestHome(app, application, page, viewModel, weightPreference, originalDispatcher, root);
            }
            catch
            {
                DispatcherProvider.SetCurrent(originalDispatcher);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(root, recursive: true);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            App.Dispose();
            DispatcherProvider.SetCurrent(_originalDispatcher);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class QueueDashboardSource(params TrainDashboardSnapshot[] snapshots) : ITrainDashboardSource
    {
        private readonly Queue<TrainDashboardSnapshot> _snapshots = new(snapshots);

        public Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshots.Count > 1 ? _snapshots.Dequeue() : _snapshots.Peek());
    }

    private sealed class ThrowingDashboardSource : ITrainDashboardSource
    {
        public Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<TrainDashboardSnapshot>(new IOException("Dashboard unavailable."));
    }

    private sealed class NoOpTrainNavigator : ITrainNavigator
    {
        public Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CachedProgressSource(ProgressSnapshot? cached) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(cached);
        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("Offline."));
        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    public sealed class MutableWeightPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current { get; private set; } = WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed;

        public void Set(WeightDisplayUnit unit)
        {
            if (Current == unit) return;
            Current = unit;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class MemoryPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class NullThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
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
}
