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
    public void Momentum_home_copy_is_complete_distinct_and_reuses_existing_primary_contracts()
    {
        var english = WorkoutResources.English;
        var thai = WorkoutResources.ForCulture(CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("Start workout", english.StartWorkout);
        Assert.Equal("Continue workout", english.ContinueWorkout);
        Assert.Equal("Try again", english.TryAgain);
        Assert.Equal("Could not repeat that workout. Try again.", english.HomeRepeatFailed);
        Assert.Equal("Workout saved. Could not open it. Tap Continue.", english.HomeOpenWorkoutFailed);
        Assert.Equal("เริ่มออกกำลังกาย", thai.StartWorkout);
        Assert.Equal("ออกกำลังกายต่อ", thai.ContinueWorkout);
        Assert.Equal("ลองอีกครั้ง", thai.TryAgain);
        Assert.Equal("เริ่มการฝึกแบบเดิมไม่สำเร็จ ลองอีกครั้ง", thai.HomeRepeatFailed);
        Assert.Equal("บันทึกการฝึกแล้ว แต่เปิดไม่สำเร็จ แตะออกกำลังกายต่อ", thai.HomeOpenWorkoutFailed);

        var englishHome = HomeCopy(english);
        var thaiHome = HomeCopy(thai);
        Assert.All(englishHome, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.All(thaiHome, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal(englishHome.Length, englishHome.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            englishHome.Zip(thaiHome).Where(pair => pair.First != english.LastBestFormat),
            pair => Assert.NotEqual(pair.First, pair.Second));
        Assert.Equal("{0} · {1}", english.LastBestFormat);
        Assert.Equal(english.LastBestFormat, thai.LastBestFormat);
        Assert.NotEqual(
            string.Format(CultureInfo.GetCultureInfo("en-US"), english.LastBestFormat, GamificationResources.English.Last, GamificationResources.English.Best),
            string.Format(CultureInfo.GetCultureInfo("th-TH"), thai.LastBestFormat, GamificationResources.Thai.Last, GamificationResources.Thai.Best));
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
        var viewAllProgress = Assert.IsType<Button>(page.FindByName("ViewAllProgressAction"));
        var contextLabel = Assert.IsType<Label>(page.FindByName("HomeContextLabel"));

        Assert.Equal("Today · Week 34", contextLabel.Text);
        Assert.True(primary.MinimumHeightRequest >= 44);
        Assert.Same(viewModel.HeroActionCommand, primary.Command);
        Assert.True(viewAllProgress.MinimumHeightRequest >= 44);
        Assert.Same(viewModel.OpenProgressCommand, viewAllProgress.Command);
        Assert.Equal(viewModel.Text.HomeViewAllData, viewAllProgress.Text);
        Assert.Single(
            Descendants(page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
        Assert.Contains(primary, Descendants(Assert.IsType<Border>(page.FindByName("HomeHero"))));
        var root = Assert.IsType<VerticalStackLayout>(
            Assert.IsType<ScrollView>(page.FindByName("MomentumHomeScroll")).Content);
        var hero = Assert.IsAssignableFrom<IView>(page.FindByName("HomeHero"));
        var repeat = Assert.IsAssignableFrom<IView>(page.FindByName("TrainAgainSection"));
        var goal = Assert.IsAssignableFrom<IView>(page.FindByName("WeeklyGoalCard"));
        var latest = Assert.IsAssignableFrom<IView>(page.FindByName("LatestPerformanceSection"));
        var children = root.Children.ToList();

        Assert.True(children.IndexOf(hero) < children.IndexOf(repeat));
        Assert.True(children.IndexOf(repeat) < children.IndexOf(goal));
        Assert.True(children.IndexOf(goal) < children.IndexOf(latest));
        Assert.Null(page.FindByName("MotivationStrip"));
        Assert.Null(page.FindByName("LevelMetric"));
        Assert.True(Assert.IsType<Border>(page.FindByName("WeeklyGoalCard")).IsVisible);
        Assert.True(Assert.IsType<Border>(page.FindByName("TrainAgainCard")).IsVisible);
        Assert.True(Assert.IsType<Border>(page.FindByName("LatestPerformanceCard")).IsVisible);
        Assert.Equal(viewModel.WeeklyGoalSentenceText,
            Assert.IsType<Label>(page.FindByName("WeeklyGoalSentence")).Text);
        Assert.Equal(viewModel.HasWeeklyStreak,
            Assert.IsType<Label>(page.FindByName("WeeklyStreakSentence")).IsVisible);
        Assert.Equal(viewModel.LatestPerformanceValue,
            Assert.IsType<Label>(page.FindByName("LatestPerformanceValue")).Text);
        Assert.Equal(viewModel.BestPerformanceValue,
            Assert.IsType<Label>(page.FindByName("BestPerformanceValue")).Text);
        var trainAgainArtwork = Assert.IsType<Border>(page.FindByName("TrainAgainArtwork"));
        var fallback = Assert.IsType<Image>(page.FindByName("TrainAgainFallback"));
        Assert.Equal("exercise_placeholder.png", Assert.IsType<FileImageSource>(fallback.Source).File);
        Assert.True(AutomationProperties.GetExcludedWithChildren(trainAgainArtwork));
        Assert.DoesNotContain(Descendants(trainAgainArtwork).OfType<Label>(), label => label.Text == "↻");
        Assert.Equal(
            viewModel.RepeatWorkoutAccessibilityText,
            SemanticProperties.GetDescription(Assert.IsType<Border>(page.FindByName("TrainAgainCard"))));
    }

    [Fact]
    public async Task Active_reload_keeps_the_same_primary_button_and_hides_only_train_again()
    {
        var source = new QueueDashboardSource(
            new TrainDashboardSnapshot(null, Repeat()),
            new TrainDashboardSnapshot(Active(), Repeat()));
        await using var context = await TestHome.CreateAsync(source, Progress());
        var primary = Assert.IsType<Button>(context.Page.FindByName("HeroActionButton"));

        await context.ViewModel.LoadAsync();

        Assert.Same(primary, context.Page.FindByName("HeroActionButton"));
        Assert.Equal("Continue workout", primary.Text);
        Assert.True(Assert.IsType<Border>(context.Page.FindByName("WeeklyGoalCard")).IsVisible);
        Assert.False(Assert.IsType<Border>(context.Page.FindByName("TrainAgainCard")).IsVisible);
        Assert.True(Assert.IsType<Border>(context.Page.FindByName("LatestPerformanceCard")).IsVisible);
        Assert.Single(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
    }

    [Fact]
    public async Task No_authoritative_progress_hides_weekly_goal_and_latest_performance_without_hiding_repeat()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, Repeat()),
            cachedProgress: null);

        Assert.False(Assert.IsType<Border>(context.Page.FindByName("WeeklyGoalCard")).IsVisible);
        Assert.True(Assert.IsType<Border>(context.Page.FindByName("TrainAgainCard")).IsVisible);
        Assert.False(Assert.IsType<Border>(context.Page.FindByName("LatestPerformanceCard")).IsVisible);
    }

    [Fact]
    public async Task Empty_repeat_hides_train_again_without_moving_authoritative_performance()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, null),
            Progress());

        Assert.True(Assert.IsType<Border>(context.Page.FindByName("WeeklyGoalCard")).IsVisible);
        Assert.False(Assert.IsType<Border>(context.Page.FindByName("TrainAgainCard")).IsVisible);
        Assert.True(Assert.IsType<Border>(context.Page.FindByName("LatestPerformanceCard")).IsVisible);
    }

    [Fact]
    public async Task Shared_unit_change_updates_the_existing_latest_performance_rows_in_pounds()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, Repeat()),
            Progress());
        var latest = Assert.IsType<Label>(context.Page.FindByName("LatestPerformanceValue"));
        var best = Assert.IsType<Label>(context.Page.FindByName("BestPerformanceValue"));
        var card = Assert.IsType<Border>(context.Page.FindByName("LatestPerformanceCard"));

        context.WeightPreference.Set(WeightDisplayUnit.Pounds);

        Assert.Equal("154.60 lb × 8 reps", latest.Text);
        Assert.Equal("159.84 lb × 6 reps", best.Text);
        Assert.Same(card, context.Page.FindByName("LatestPerformanceCard"));
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
        Assert.Same(context.Application.Resources["TrackZSecondaryButtonStyle"], retry.Style);
        Assert.Single(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
    }

    [Fact]
    public async Task Repeat_failure_hides_load_retry_and_keeps_train_again_as_the_retry_action()
    {
        await using var context = await TestHome.CreateAsync(
            new TrainDashboardSnapshot(null, InvalidRepeat()),
            cachedProgress: null);
        var retry = Assert.IsType<Button>(context.Page.FindByName("HomeRetryButton"));
        var trainAgain = Assert.IsType<Border>(context.Page.FindByName("TrainAgainCard"));

        await context.ViewModel.TrainAgainCommand.ExecuteAsync();

        Assert.Equal("Could not repeat that workout. Try again.", context.ViewModel.ErrorText);
        Assert.False(context.ViewModel.HasLoadRetry);
        Assert.False(retry.IsVisible);
        Assert.False(context.ViewModel.RetryCommand.CanExecute(null));
        Assert.True(trainAgain.IsVisible);
        Assert.True(context.ViewModel.TrainAgainCommand.CanExecute(null));
        Assert.Single(
            Descendants(context.Page).OfType<Button>(),
            button => ReferenceEquals(button.Style, context.Application.Resources["TrackZPrimaryButtonStyle"]));
    }

    private static string[] HomeCopy(WorkoutTextSet text) =>
    [
        text.ReadyWhenYouAre,
        text.YouAreInMotion,
        text.StartTraining,
        text.ChooseTodaysWorkout,
        text.ChooseWorkoutSupporting,
        text.WorkoutInProgress,
        text.ContinueWorkout,
        text.TrainAgain,
        text.Open,
        text.HomeWeeklyGoalFormat,
        text.HomeWeeklyStreakFormat,
        text.HomeLatestPerformance,
        text.HomeLatestLabel,
        text.HomeBestLabel,
        text.HomeWeightedValueFormat,
        text.HomeAssistedValueFormat,
        text.HomeBodyweightValueFormat,
        text.HomeExerciseProgressFormat,
        text.RepeatWorkoutAccessibilityFormat,
        text.HomeLoadFailed,
        text.HomeRepeatFailed,
        text.HomeOpenWorkoutFailed,
        text.HomeContextFormat
    ];

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
