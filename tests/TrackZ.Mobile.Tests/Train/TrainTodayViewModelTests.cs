using System.Globalization;
using TrackZ.Contracts.Gamification;
using TrackZ.Contracts.Progress;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Train;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Train;

public sealed class TrainTodayViewModelTests
{
    [Fact]
    public async Task Hero_opens_picker_without_active_workout_and_active_workout_when_active()
    {
        var noActiveNavigator = new RecordingTrainNavigator();
        var noActive = CreateCommandViewModel(active: null, repeat: null, noActiveNavigator);
        await noActive.LoadAsync();

        Assert.True(noActive.ShowStartHero);
        Assert.False(noActive.ShowContinueHero);
        Assert.False(noActive.ShowTrainAgain);
        await noActive.HeroActionCommand.ExecuteAsync();
        Assert.Equal(["picker"], noActiveNavigator.Events);

        var activeNavigator = new RecordingTrainNavigator();
        var active = CreateCommandViewModel(ActiveCard(), RepeatShortcut(), activeNavigator);
        await active.LoadAsync();

        Assert.False(active.ShowStartHero);
        Assert.True(active.ShowContinueHero);
        Assert.False(active.ShowTrainAgain);
        await active.HeroActionCommand.ExecuteAsync();
        Assert.Equal(["active-workout"], activeNavigator.Events);
    }

    [Fact]
    public async Task View_all_progress_routes_only_when_recent_performance_exists()
    {
        var navigator = new RecordingTrainNavigator();
        var viewModel = new TrainTodayViewModel(
            new RecordingTrainDashboardSource(new TrainDashboardSnapshot(null, null)),
            new AccountSessionBoundary(),
            WorkoutResources.English,
            new CachedProgressSource(Snapshot(3, 1, 4, 8, 640)),
            new FixedConnectivity(false),
            new MutableWeightPreference(),
            GamificationResources.English,
            navigator: navigator);
        await viewModel.LoadAsync();

        Assert.True(viewModel.OpenProgressCommand.CanExecute(null));
        await viewModel.OpenProgressCommand.ExecuteAsync();

        Assert.Equal(["progress:77777777-7777-7777-7777-777777777777"], navigator.Events);
    }

    [Theory]
    [InlineData("en-US", "Couldn't open progress. Try again.")]
    [InlineData("th-TH", "เปิดข้อมูลผลงานไม่สำเร็จ ลองอีกครั้ง")]
    public async Task Progress_navigation_failure_is_localized_and_the_quiet_retry_opens_the_same_exercise(
        string cultureName,
        string expectedError)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var navigator = new FailOnceProgressNavigator();
        var viewModel = new TrainTodayViewModel(
            new RecordingTrainDashboardSource(new TrainDashboardSnapshot(null, null)),
            new AccountSessionBoundary(),
            WorkoutResources.ForCulture(culture),
            new CachedProgressSource(Snapshot(3, 1, 4, 8, 640)),
            new FixedConnectivity(false),
            new MutableWeightPreference(),
            culture.TwoLetterISOLanguageName == "th"
                ? GamificationResources.Thai
                : GamificationResources.English,
            navigator: navigator);
        await viewModel.LoadAsync();

        var failure = await Record.ExceptionAsync(() => viewModel.OpenProgressCommand.ExecuteAsync());

        Assert.Null(failure);
        Assert.Equal(expectedError, viewModel.ErrorText);
        Assert.True(viewModel.HasLoadRetry);
        Assert.True(viewModel.RetryCommand.CanExecute(null));

        await viewModel.RetryCommand.ExecuteAsync();

        Assert.Equal(2, navigator.OpenProgressCount);
        Assert.Equal(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            navigator.LastExerciseId);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.HasLoadRetry);
        Assert.False(viewModel.RetryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Account_reset_cancels_progress_navigation_without_leaking_cancellation_or_error_state()
    {
        var boundary = new AccountSessionBoundary();
        var navigator = new CancellableProgressNavigator();
        var viewModel = new TrainTodayViewModel(
            new RecordingTrainDashboardSource(new TrainDashboardSnapshot(null, null)),
            boundary,
            WorkoutResources.English,
            new CachedProgressSource(Snapshot(3, 1, 4, 8, 640)),
            new FixedConnectivity(false),
            new MutableWeightPreference(),
            GamificationResources.English,
            navigator: navigator);
        await viewModel.LoadAsync();

        var navigation = viewModel.OpenProgressCommand.ExecuteAsync();
        await navigator.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var reset = boundary.ResetAsync(_ => Task.CompletedTask);

        await Task.WhenAll(navigation, reset).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Null(viewModel.ErrorText);
        Assert.False(viewModel.HasLoadRetry);
        Assert.False(viewModel.RetryCommand.CanExecute(null));
        Assert.Null(viewModel.RecentMomentum);
    }

    [Fact]
    public async Task Train_again_is_available_only_after_a_ready_snapshot_commits()
    {
        var source = new GatedTrainDashboardSource();
        var navigator = new RecordingTrainNavigator();
        var viewModel = CreateCommandViewModel(source, navigator);

        var load = viewModel.LoadAsync();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(viewModel.CanMutate);
        Assert.False(viewModel.HeroActionCommand.CanExecute(null));
        Assert.False(viewModel.TrainAgainCommand.CanExecute(null));

        source.Release.TrySetResult(new TrainDashboardSnapshot(null, RepeatShortcut()));
        await load;

        Assert.True(viewModel.CanMutate);
        Assert.True(viewModel.ShowStartHero);
        Assert.True(viewModel.ShowTrainAgain);
    }

    [Fact]
    public async Task Local_state_commits_before_gated_progress_refresh_and_cached_values_survive_failure()
    {
        var source = new RecordingTrainDashboardSource(new(
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), At(9), [BodyPart.Chest], 2, 1, 4),
            null));
        var progress = new CachedThenGatedFailingProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640));
        var viewModel = CreateViewModel(source, progress, online: true);

        var load = viewModel.LoadAsync();
        await progress.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(viewModel.ActiveWorkout);
        Assert.Equal(3, viewModel.WeeklyCompletedWorkouts);
        Assert.Equal(0.75d, viewModel.WeeklyProgress, 3);
        progress.FailRefresh();
        await load;

        Assert.True(viewModel.HasAuthoritativeProgress);
        Assert.Equal(0.75d, viewModel.WeeklyProgress, 3);
    }

    [Fact]
    public async Task No_cache_offline_hides_motivation_instead_of_fabricating_zeroes()
    {
        var viewModel = CreateViewModel(new RecordingTrainDashboardSource(new(null, null)), new EmptyProgressSource(), online: false);

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.False(viewModel.HasRecentMomentum);
    }

    [Fact]
    public async Task Known_offline_cache_read_never_shows_progress_loading_while_local_state_commits()
    {
        var progress = new GatedCachedProgressSource();
        var activeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(
                new(activeId, At(9), [BodyPart.Chest], 2, 1, 4),
                null)),
            progress,
            online: false);
        var loadingStates = new List<bool>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TrainTodayViewModel.IsProgressLoading))
                loadingStates.Add(viewModel.IsProgressLoading);
        };

        var load = viewModel.LoadAsync();
        await progress.CacheEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(activeId, viewModel.ActiveWorkout?.WorkoutId);
        Assert.False(viewModel.IsProgressLoading);
        Assert.DoesNotContain(true, loadingStates);
        progress.Release(null);
        await load;
        Assert.False(viewModel.IsProgressLoading);
        Assert.DoesNotContain(true, loadingStates);
    }

    [Fact]
    public async Task Going_offline_during_gated_refresh_hides_loading_notifies_state_and_cannot_restore_after_reset()
    {
        var boundary = new AccountSessionBoundary();
        var connectivity = new MutableConnectivity(online: true);
        var progress = new CachedThenGatedProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640));
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(
                new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), At(9), [BodyPart.Chest], 2, 1, 4),
                new(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), [BodyPart.Back], At(8), 2, 4, null,
                    [new WorkoutExerciseSelection(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), TrackingMode.Weighted)]))),
            progress,
            connectivity,
            boundary);
        var load = viewModel.LoadAsync();
        await progress.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(viewModel.IsProgressLoading);

        var propertyChanges = new List<string?>();
        var offlineChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, args) =>
        {
            propertyChanges.Add(args.PropertyName);
            if (args.PropertyName == nameof(TrainTodayViewModel.IsOffline)) offlineChanged.TrySetResult();
        };

        connectivity.SetOnline(false);
        await offlineChanged.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(viewModel.IsOffline);
        Assert.False(viewModel.IsProgressLoading);
        Assert.Contains(nameof(TrainTodayViewModel.IsOffline), propertyChanges);
        await boundary.ResetAsync(_ => Task.CompletedTask);
        progress.Release(Snapshot(goal: 5, done: 5, streak: 9, level: 99, xp: 9999));
        await load;

        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.Null(viewModel.ActiveWorkout);
        Assert.Null(viewModel.RepeatWorkout);
    }

    [Fact]
    public async Task Progress_cache_read_failure_keeps_local_workout_and_hides_unauthoritative_motivation()
    {
        var source = new RecordingTrainDashboardSource(new(
            new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), At(9), [BodyPart.Chest], 2, 1, 4),
            null));
        var viewModel = CreateViewModel(source, new ThrowingProgressSource(), online: false);

        await viewModel.LoadAsync();

        Assert.NotNull(viewModel.ActiveWorkout);
        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.Null(viewModel.ErrorText);
    }

    [Fact]
    public async Task Home_dashboard_load_failure_uses_the_home_specific_localized_error()
    {
        var viewModel = new TrainTodayViewModel(
            new ThrowingTrainDashboardSource(),
            new AccountSessionBoundary(),
            WorkoutResources.English);

        await viewModel.LoadAsync();

        Assert.Equal("Could not load Home", viewModel.ErrorText);
        Assert.NotEqual(WorkoutResources.English.LoadFailed, viewModel.ErrorText);
    }

    [Fact]
    public async Task Real_local_sqlite_failure_is_contained_and_disables_dashboard_mutation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trackz-home-load-failure-{Guid.NewGuid():N}");
        var invalidDatabasePath = Path.Combine(root, "workouts.db");
        Directory.CreateDirectory(invalidDatabasePath);
        try
        {
            var source = new LocalTrainDashboardSource(
                new LocalWorkoutRepository(new TrackZLocalDatabase(invalidDatabasePath)),
                new ExerciseCache(Path.Combine(root, "exercises.db")));
            var viewModel = new TrainTodayViewModel(
                source,
                new AccountSessionBoundary(),
                WorkoutResources.English);

            var failure = await Record.ExceptionAsync(() => viewModel.LoadAsync());

            Assert.Null(failure);
            Assert.Equal("Could not load Home", viewModel.ErrorText);
            Assert.False(viewModel.CanMutate);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_dashboard_exposes_retry_and_successful_retry_clears_error()
    {
        var source = new FailOnceTrainDashboardSource(new TrainDashboardSnapshot(null, RepeatShortcut()));
        var viewModel = new TrainTodayViewModel(
            source,
            new AccountSessionBoundary(),
            WorkoutResources.English);

        await viewModel.LoadAsync();

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.HasLoadRetry);
        Assert.Equal("Could not load Home", viewModel.ErrorText);
        Assert.False(viewModel.CanMutate);
        Assert.True(viewModel.RetryCommand.CanExecute(null));

        await viewModel.RetryCommand.ExecuteAsync();

        Assert.False(viewModel.HasError);
        Assert.False(viewModel.HasLoadRetry);
        Assert.Null(viewModel.ErrorText);
        Assert.True(viewModel.ShowTrainAgain);
        Assert.True(viewModel.CanMutate);
        Assert.Equal(2, source.LoadCount);
    }

    [Fact]
    public async Task Caller_cancellation_during_local_dashboard_read_is_propagated_without_an_error_state()
    {
        var source = new CancellableTrainDashboardSource();
        var viewModel = new TrainTodayViewModel(
            source,
            new AccountSessionBoundary(),
            WorkoutResources.English);
        using var cancellation = new CancellationTokenSource();

        var load = viewModel.LoadAsync(cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.False(viewModel.HasError);
        Assert.Null(viewModel.ErrorText);
        Assert.False(viewModel.CanMutate);
    }

    [Fact]
    public async Task Caller_cancellation_during_progress_read_is_propagated()
    {
        var progress = new GatedCancellableProgressSource();
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(null, null)),
            progress,
            online: false);
        using var cancellation = new CancellationTokenSource();

        var load = viewModel.LoadAsync(cancellation.Token);
        await progress.CacheEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
    }

    [Fact]
    public async Task Recent_momentum_uses_latest_time_then_descending_exercise_id()
    {
        var earliest = ProgressRecord("11111111-1111-1111-1111-111111111111", "Early", At(8));
        var lowerTie = ProgressRecord("22222222-2222-2222-2222-222222222222", "Lower tie", At(10));
        var higherTie = ProgressRecord("33333333-3333-3333-3333-333333333333", "Higher tie", At(10));
        var snapshot = Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640) with
        {
            Summary = new ProgressSummaryDto(1000m, 500m, 3, 1, [earliest, lowerTie, higherTie])
        };
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(null, null)),
            new CachedProgressSource(snapshot),
            online: false);

        await viewModel.LoadAsync();

        Assert.Equal("Higher tie", viewModel.RecentMomentum?.ExerciseName);
    }

    [Fact]
    public async Task Home_progress_presentation_uses_plain_language_facts()
    {
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(null, null)),
            new CachedProgressSource(Snapshot(goal: 3, done: 1, streak: 4, level: 8, xp: 640)),
            online: false);

        await viewModel.LoadAsync();

        Assert.Equal("This week you completed 1 of 3 workouts", viewModel.WeeklyGoalSentenceText);
        Assert.Equal("Goal met 4 weeks in a row", viewModel.WeeklyStreakSentenceText);
        Assert.True(viewModel.HasWeeklyStreak);
        Assert.Equal("Latest performance", viewModel.Text.HomeLatestPerformance);
        Assert.Equal("Bench Press", viewModel.LatestPerformanceTitle);
        Assert.Equal("70.125 kg × 8 reps", viewModel.LatestPerformanceValue);
        Assert.Equal("72.5 kg × 6 reps", viewModel.BestPerformanceValue);
    }

    [Fact]
    public async Task Account_reset_after_cached_progress_prevents_delayed_refresh_from_restoring_home_state()
    {
        var boundary = new AccountSessionBoundary();
        var source = new RecordingTrainDashboardSource(new(
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"), At(9), [BodyPart.Chest], 2, 1, 4),
            new(Guid.Parse("55555555-5555-5555-5555-555555555555"), [BodyPart.Back], At(8), 2, 4, null,
                [new WorkoutExerciseSelection(Guid.Parse("66666666-6666-6666-6666-666666666666"), TrackingMode.Weighted)])));
        var progress = new CachedThenGatedProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640));
        var viewModel = CreateViewModel(source, progress, online: true, boundary);

        var load = viewModel.LoadAsync();
        await progress.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await boundary.ResetAsync(_ => Task.CompletedTask);
        progress.Release(Snapshot(goal: 5, done: 5, streak: 9, level: 99, xp: 9999));
        await load;

        Assert.Null(viewModel.ActiveWorkout);
        Assert.Null(viewModel.RepeatWorkout);
        Assert.False(viewModel.HasAuthoritativeProgress);
        Assert.Equal(0d, viewModel.WeeklyProgress);
        Assert.False(viewModel.HasRecentMomentum);
    }
    [Fact]
    public async Task Today_loads_active_workout_and_exact_repeat_shortcut()
    {
        var activeId = Guid.NewGuid();
        var repeatId = Guid.NewGuid();
        var source = new RecordingTrainDashboardSource(new(
            new(activeId, At(9), [BodyPart.Chest], 2, 1, 4),
            new(repeatId, [BodyPart.Shoulders, BodyPart.Back], At(8), 6, 18,
                "/cache/shoulder.png", [
                    new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted),
                    new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Assisted)
                ])));
        var viewModel = new TrainTodayViewModel(
            source,
            new AccountSessionBoundary(),
            WorkoutResources.English);

        await viewModel.LoadAsync();

        Assert.Equal(activeId, viewModel.ActiveWorkout?.WorkoutId);
        Assert.Equal(4, viewModel.ActiveWorkout?.LoggedSetCount);
        Assert.Equal(repeatId, viewModel.RepeatWorkout?.SourceWorkoutId);
        Assert.Equal("Shoulders + Back", viewModel.RepeatWorkoutTitle);
        Assert.Equal(1, source.LoadCount);
    }

    [Theory]
    [InlineData("en-US", "Shoulders + Back")]
    [InlineData("th-TH", "ไหล่ + หลัง")]
    public async Task Repeat_title_uses_current_localized_body_part_names(
        string cultureName,
        string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var source = new RecordingTrainDashboardSource(new(
                null,
                new(Guid.NewGuid(), [BodyPart.Shoulders, BodyPart.Back], At(8), 2, 1, null,
                    [new WorkoutExerciseSelection(Guid.NewGuid(), TrackingMode.Weighted)])));
            var viewModel = new TrainTodayViewModel(
                source,
                new AccountSessionBoundary(),
                WorkoutResources.Current);

            await viewModel.LoadAsync();

            Assert.Equal(expected, viewModel.RepeatWorkoutTitle);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData(
        "en-US",
        "Ready when you are.",
        "Start training",
        "Choose today's workout",
        "You choose the body area and exercises",
        "This week you completed 3 of 4 workouts",
        "Goal met 4 weeks in a row",
        "Shoulders + Back",
        "Thursday · 2 exercises · 3 sets logged",
        "Train again: Shoulders + Back, 2 exercises, 3 sets",
        "70.125 kg × 8 reps",
        "72.5 kg × 6 reps",
        "154.60 lb × 8 reps",
        "159.84 lb × 6 reps")]
    [InlineData(
        "th-TH",
        "พร้อมเมื่อไหร่ เริ่มได้เลย",
        "เริ่มฝึก",
        "เลือกการฝึกวันนี้",
        "คุณเลือกส่วนร่างกายและท่าออกกำลังกายเอง",
        "สัปดาห์นี้ฝึกแล้ว 3 จากเป้าหมาย 4 ครั้ง",
        "ทำถึงเป้า 4 สัปดาห์ติด",
        "ไหล่ + หลัง",
        "วันพฤหัสบดี · 2 ท่า · บันทึกแล้ว 3 เซ็ต",
        "ฝึกแบบเดิมอีกครั้ง: ไหล่ + หลัง, 2 ท่า, 3 เซ็ต",
        "70.125 กก. × 8 ครั้ง",
        "72.5 กก. × 6 ครั้ง",
        "154.60 ปอนด์ × 8 ครั้ง",
        "159.84 ปอนด์ × 6 ครั้ง")]
    public async Task Ready_home_presentation_uses_literal_localized_copy_and_formats(
        string cultureName,
        string headline,
        string eyebrow,
        string title,
        string supporting,
        string weeklyGoalSentence,
        string weeklyStreakSentence,
        string repeatTitle,
        string repeatMeta,
        string repeatAccessibility,
        string latest,
        string best,
        string latestPounds,
        string bestPounds)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var repeat = new RepeatWorkoutShortcut(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                [BodyPart.Shoulders, BodyPart.Back],
                At(8),
                2,
                3,
                null,
                [new WorkoutExerciseSelection(Guid.Parse("66666666-6666-6666-6666-666666666666"), TrackingMode.Weighted)]);
            var weightPreference = new MutableWeightPreference();
            var viewModel = new TrainTodayViewModel(
                new RecordingTrainDashboardSource(new(null, repeat)),
                new AccountSessionBoundary(),
                WorkoutResources.ForCulture(culture),
                new CachedProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640)),
                new FixedConnectivity(false),
                weightPreference,
                culture.TwoLetterISOLanguageName == "th" ? GamificationResources.Thai : GamificationResources.English);

            await viewModel.LoadAsync();

            Assert.Equal(headline, viewModel.HomeHeadlineText);
            Assert.Equal(eyebrow, viewModel.HeroEyebrowText);
            Assert.Equal(title, viewModel.HeroTitleText);
            Assert.Equal(supporting, viewModel.HeroSupportingText);
            Assert.Equal(weeklyGoalSentence, viewModel.WeeklyGoalSentenceText);
            Assert.Equal(weeklyStreakSentence, viewModel.WeeklyStreakSentenceText);
            Assert.Equal(repeatTitle, viewModel.RepeatWorkoutTitle);
            Assert.Equal(repeatMeta, viewModel.RepeatWorkoutMetaText);
            Assert.Equal(repeatAccessibility, viewModel.RepeatWorkoutAccessibilityText);
            Assert.Equal(latest, viewModel.LatestPerformanceValue);
            Assert.Equal(best, viewModel.BestPerformanceValue);
            weightPreference.Set(WeightDisplayUnit.Pounds);
            Assert.Equal(latestPounds, viewModel.LatestPerformanceValue);
            Assert.Equal(bestPounds, viewModel.BestPerformanceValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("en-US", "You're in motion.", "Workout in progress", "Chest + Arms", "3 of 5 exercises logged · 8 sets", "Continue workout")]
    [InlineData("th-TH", "กำลังไปได้ดี", "กำลังออกกำลังกาย", "หน้าอก + แขน", "บันทึกแล้ว 3 จาก 5 ท่า · 8 เซ็ต", "ออกกำลังกายต่อ")]
    public async Task Active_home_presentation_uses_the_same_localized_hero_contract(
        string cultureName,
        string headline,
        string eyebrow,
        string title,
        string supporting,
        string action)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var viewModel = new TrainTodayViewModel(
                new RecordingTrainDashboardSource(new(
                    new(Guid.Parse("77777777-7777-7777-7777-777777777777"), At(9), [BodyPart.Chest, BodyPart.Arms], 5, 3, 8),
                    null)),
                new AccountSessionBoundary(),
                WorkoutResources.ForCulture(culture));

            await viewModel.LoadAsync();

            Assert.Equal(headline, viewModel.HomeHeadlineText);
            Assert.Equal(eyebrow, viewModel.HeroEyebrowText);
            Assert.Equal(title, viewModel.HeroTitleText);
            Assert.Equal(supporting, viewModel.HeroSupportingText);
            Assert.Equal(action, viewModel.HeroActionText);
            Assert.Equal(0.6d, viewModel.ActiveWorkoutProgress);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("en-US", "Today · Week 34", "Today · Week 35")]
    [InlineData("th-TH", "วันนี้ · สัปดาห์ที่ 34", "วันนี้ · สัปดาห์ที่ 35")]
    public async Task Home_context_uses_the_injected_clock_and_notifies_on_each_reload(
        string cultureName,
        string week34,
        string week35)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var clock = new MutableClock(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
            var viewModel = new TrainTodayViewModel(
                new RecordingTrainDashboardSource(new(null, null)),
                new AccountSessionBoundary(),
                WorkoutResources.ForCulture(culture),
                progress: null,
                connectivity: null,
                weightUnits: new MutableWeightPreference(),
                gamificationText: culture.TwoLetterISOLanguageName == "th" ? GamificationResources.Thai : GamificationResources.English,
                clock: clock,
                localTimeZone: TimeZoneInfo.Utc);
            var contextChanges = 0;
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TrainTodayViewModel.HomeContextText)) contextChanges++;
            };

            await viewModel.LoadAsync();
            Assert.Equal(week34, viewModel.HomeContextText);
            clock.UtcNow = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
            await viewModel.LoadAsync();

            Assert.Equal(week35, viewModel.HomeContextText);
            Assert.Equal(2, contextChanges);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public void Home_context_uses_the_injected_device_timezone_at_an_iso_week_boundary()
    {
        var bangkok = TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Bangkok",
            TimeSpan.FromHours(7),
            "Asia/Bangkok",
            "Asia/Bangkok");
        var sundayUtcMondayLocal = new DateTimeOffset(2026, 8, 23, 17, 30, 0, TimeSpan.Zero);
        Assert.Equal(34, ISOWeek.GetWeekOfYear(sundayUtcMondayLocal.UtcDateTime));
        var viewModel = new TrainTodayViewModel(
            new RecordingTrainDashboardSource(new(null, null)),
            new AccountSessionBoundary(),
            WorkoutResources.English,
            progress: null,
            connectivity: null,
            weightUnits: new MutableWeightPreference(),
            gamificationText: GamificationResources.English,
            clock: new MutableClock(sundayUtcMondayLocal),
            localTimeZone: bangkok);

        Assert.Equal("Today · Week 35", viewModel.HomeContextText);
    }

    [Theory]
    [InlineData("en-US", "th-TH", "วันจันทร์ · 2 ท่า · บันทึกแล้ว 3 เซ็ต")]
    [InlineData("th-TH", "en-US", "Monday · 2 exercises · 3 sets logged")]
    public async Task Repeat_completion_weekday_uses_device_timezone_and_current_ui_culture(
        string currentCultureName,
        string currentUiCultureName,
        string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var currentCulture = CultureInfo.GetCultureInfo(currentCultureName);
            var uiCulture = CultureInfo.GetCultureInfo(currentUiCultureName);
            CultureInfo.CurrentCulture = currentCulture;
            CultureInfo.CurrentUICulture = uiCulture;
            var bangkok = TimeZoneInfo.CreateCustomTimeZone(
                "Asia/Bangkok",
                TimeSpan.FromHours(7),
                "Asia/Bangkok",
                "Asia/Bangkok");
            var sundayUtcMondayLocal = new DateTimeOffset(2026, 8, 23, 17, 30, 0, TimeSpan.Zero);
            var repeat = RepeatShortcut() with
            {
                CompletedAt = sundayUtcMondayLocal,
                ExerciseCount = 2,
                LoggedSetCount = 3
            };
            var viewModel = new TrainTodayViewModel(
                new RecordingTrainDashboardSource(new(null, repeat)),
                new AccountSessionBoundary(),
                WorkoutResources.ForCulture(uiCulture),
                progress: null,
                connectivity: null,
                weightUnits: new MutableWeightPreference(),
                gamificationText: uiCulture.TwoLetterISOLanguageName == "th"
                    ? GamificationResources.Thai
                    : GamificationResources.English,
                localTimeZone: bangkok);

            await viewModel.LoadAsync();

            Assert.Equal(expected, viewModel.RepeatWorkoutMetaText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Fact]
    public async Task Presentation_properties_notify_when_loaded_and_when_the_account_resets()
    {
        var boundary = new AccountSessionBoundary();
        var viewModel = CreateViewModel(
            new RecordingTrainDashboardSource(new(
                new(Guid.Parse("88888888-8888-8888-8888-888888888888"), At(9), [BodyPart.Chest], 5, 3, 8),
                new(Guid.Parse("99999999-9999-9999-9999-999999999999"), [BodyPart.Back], At(8), 2, 3, null,
                    [new WorkoutExerciseSelection(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), TrackingMode.Weighted)]))),
            new CachedProgressSource(Snapshot(goal: 4, done: 3, streak: 4, level: 8, xp: 640)),
            online: false,
            boundary);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.LoadAsync();
        await boundary.ResetAsync(_ => Task.CompletedTask);

        var derivedProperties = new[]
        {
            nameof(TrainTodayViewModel.HomeHeadlineText),
            nameof(TrainTodayViewModel.HeroEyebrowText),
            nameof(TrainTodayViewModel.HeroTitleText),
            nameof(TrainTodayViewModel.HeroSupportingText),
            nameof(TrainTodayViewModel.ActiveWorkoutProgress),
            nameof(TrainTodayViewModel.WeeklyProgress),
            nameof(TrainTodayViewModel.WeeklyGoalSentenceText),
            nameof(TrainTodayViewModel.HasWeeklyStreak),
            nameof(TrainTodayViewModel.WeeklyStreakSentenceText),
            nameof(TrainTodayViewModel.RepeatWorkoutTitle),
            nameof(TrainTodayViewModel.RepeatWorkoutMetaText),
            nameof(TrainTodayViewModel.RepeatWorkoutAccessibilityText),
            nameof(TrainTodayViewModel.LatestPerformanceTitle),
            nameof(TrainTodayViewModel.LatestPerformanceValue),
            nameof(TrainTodayViewModel.BestPerformanceValue)
        };
        Assert.All(derivedProperties, property =>
            Assert.True(changes.Count(change => change == property) >= 2, $"{property} did not notify for load and reset."));
    }

    [Fact]
    public async Task Account_reset_during_load_does_not_commit_stale_dashboard()
    {
        var boundary = new AccountSessionBoundary();
        var source = new GatedTrainDashboardSource();
        var viewModel = new TrainTodayViewModel(source, boundary, WorkoutResources.English);
        var load = viewModel.LoadAsync();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await boundary.ResetAsync(_ => Task.CompletedTask);
        source.Release.TrySetResult(new TrainDashboardSnapshot(
            new(Guid.NewGuid(), At(9), [BodyPart.Chest], 1, 1, 1),
            null));
        await load;

        Assert.Null(viewModel.ActiveWorkout);
        Assert.Null(viewModel.RepeatWorkout);
    }

    private static DateTimeOffset At(int hour) =>
        new(2026, 8, 20, hour, 0, 0, TimeSpan.Zero);

    private static TrainTodayViewModel CreateViewModel(
        ITrainDashboardSource source,
        IProgressSnapshotSource progress,
        bool online,
        IAccountSessionBoundary? boundary = null) =>
        CreateViewModel(source, progress, new FixedConnectivity(online), boundary);

    private static TrainTodayViewModel CreateViewModel(
        ITrainDashboardSource source,
        IProgressSnapshotSource progress,
        IConnectivityService connectivity,
        IAccountSessionBoundary? boundary = null) => new(
        source,
        boundary ?? new AccountSessionBoundary(),
        WorkoutResources.English,
        progress,
        connectivity,
        new MutableWeightPreference(),
        GamificationResources.English);

    private static TrainTodayViewModel CreateCommandViewModel(
        ActiveWorkoutCard? active,
        RepeatWorkoutShortcut? repeat,
        RecordingTrainNavigator navigator) =>
        CreateCommandViewModel(new RecordingTrainDashboardSource(new(active, repeat)), navigator);

    private static TrainTodayViewModel CreateCommandViewModel(
        ITrainDashboardSource source,
        RecordingTrainNavigator navigator) => new(
        source,
        new AccountSessionBoundary(),
        WorkoutResources.English,
        progress: null,
        connectivity: null,
        weightUnits: new MutableWeightPreference(),
        gamificationText: GamificationResources.English,
        activeWorkouts: null,
        navigator: navigator);

    private static ActiveWorkoutCard ActiveCard() => new(
        Guid.Parse("88888888-8888-8888-8888-888888888888"),
        At(9),
        [BodyPart.Back],
        2,
        1,
        4);

    private static RepeatWorkoutShortcut RepeatShortcut() => new(
        Guid.Parse("99999999-9999-9999-9999-999999999999"),
        [BodyPart.Chest],
        At(8),
        2,
        3,
        null,
        [new WorkoutExerciseSelection(
            Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), TrackingMode.Weighted)]);

    private static ProgressSnapshot Snapshot(int goal, int done, int streak, int level, int xp) => new(
        new ProgressSummaryDto(
            1000m,
            500m,
            done,
            1,
            [new ExerciseProgressSummaryDto(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "Bench Press",
                TrackingMode.Weighted,
                At(10),
                70.125m,
                null,
                8,
                72.5m,
                null,
                6)]),
        new GamificationProfileDto(xp, level, 600, 800, goal, done, streak, streak, [], []),
        At(11));

    private static ExerciseProgressSummaryDto ProgressRecord(string id, string name, DateTimeOffset performedAt) => new(
        Guid.Parse(id), name, TrackingMode.Weighted, performedAt, 70m, null, 8, 72m, null, 6);

    private sealed class RecordingTrainDashboardSource(TrainDashboardSnapshot snapshot)
        : ITrainDashboardSource
    {
        public int LoadCount { get; private set; }

        public Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingTrainDashboardSource : ITrainDashboardSource
    {
        public Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<TrainDashboardSnapshot>(new IOException("Dashboard unavailable."));
    }

    private sealed class FailOnceTrainDashboardSource(TrainDashboardSnapshot success)
        : ITrainDashboardSource
    {
        public int LoadCount { get; private set; }

        public Task<TrainDashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return LoadCount == 1
                ? Task.FromException<TrainDashboardSnapshot>(new IOException("Dashboard unavailable."))
                : Task.FromResult(success);
        }
    }

    private sealed class CancellableTrainDashboardSource : ITrainDashboardSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new TrainDashboardSnapshot(null, null);
        }
    }

    private sealed class GatedTrainDashboardSource : ITrainDashboardSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TrainDashboardSnapshot> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TrainDashboardSnapshot> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class EmptyProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(null);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("No cached progress."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class CachedProgressSource(ProgressSnapshot snapshot) : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(snapshot);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class GatedCachedProgressSource : IProgressSnapshotSource
    {
        private readonly TaskCompletionSource<ProgressSnapshot?> _cached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CacheEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
        {
            CacheEntered.TrySetResult();
            return _cached.Task;
        }

        public void Release(ProgressSnapshot? snapshot) => _cached.TrySetResult(snapshot);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("Offline refresh is not allowed."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class ThrowingProgressSource : IProgressSnapshotSource
    {
        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot?>(new InvalidOperationException("Progress cache unavailable."));

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("Progress unavailable."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class GatedCancellableProgressSource : IProgressSnapshotSource
    {
        public TaskCompletionSource CacheEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default)
        {
            CacheEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<ProgressSnapshot>(new InvalidOperationException("Not reached."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class CachedThenGatedFailingProgressSource(ProgressSnapshot cached) : IProgressSnapshotSource
    {
        private readonly TaskCompletionSource<ProgressSnapshot> _refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(cached);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshEntered.TrySetResult();
            return _refresh.Task;
        }

        public void FailRefresh() => _refresh.TrySetException(new InvalidOperationException("Refresh unavailable."));

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class CachedThenGatedProgressSource(ProgressSnapshot cached) : IProgressSnapshotSource
    {
        private readonly TaskCompletionSource<ProgressSnapshot> _refresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProgressSnapshot?> GetCachedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ProgressSnapshot?>(cached);

        public Task<ProgressSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshEntered.TrySetResult();
            return _refresh.Task;
        }

        public void Release(ProgressSnapshot refreshed) => _refresh.TrySetResult(refreshed);

        public Task<ProgressSnapshot> UpdateWeeklyGoalAsync(int weeklyGoal, CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);
    }

    private sealed class FixedConnectivity(bool online) : IConnectivityService
    {
        public bool IsOnline => online;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MutableConnectivity(bool online) : IConnectivityService
    {
        public bool IsOnline { get; private set; } = online;
        public event EventHandler? ConnectivityChanged;

        public void SetOnline(bool online)
        {
            if (IsOnline == online) return;
            IsOnline = online;
            ConnectivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class MutableWeightPreference : IWeightUnitPreference
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

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class RecordingTrainNavigator : ITrainNavigator
    {
        public List<string> Events { get; } = [];

        public Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("picker");
            return Task.CompletedTask;
        }

        public Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("active-workout");
            return Task.CompletedTask;
        }

        public Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default)
        {
            Events.Add($"progress:{exerciseId:D}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailOnceProgressNavigator : ITrainNavigator
    {
        public int OpenProgressCount { get; private set; }
        public Guid? LastExerciseId { get; private set; }

        public Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OpenProgressAsync(Guid exerciseId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenProgressCount++;
            LastExerciseId = exerciseId;
            return OpenProgressCount == 1
                ? Task.FromException(new InvalidOperationException("Progress navigation failed."))
                : Task.CompletedTask;
        }
    }

    private sealed class CancellableProgressNavigator : ITrainNavigator
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenWorkoutPickerAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OpenActiveWorkoutAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task OpenProgressAsync(
            Guid exerciseId,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
