using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Workout;
using System.Net;
using System.Text;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class ExercisePickerViewModelTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"trackz-picker-{Guid.NewGuid():N}.db");
    private ExerciseCache _cache = null!;

    public Task InitializeAsync()
    {
        _cache = new ExerciseCache(_databasePath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    public static TheoryData<string, bool, bool, BodyPart?, ExercisePickerPresentationState, int> PickerCases => new()
    {
        { "results", true, false, null, ExercisePickerPresentationState.Results, 1 },
        { "authentication", true, false, null, ExercisePickerPresentationState.AuthenticationRequired, 0 },
        { "request-failure", true, false, null, ExercisePickerPresentationState.RequestFailure, 0 },
        { "timeout", true, false, null, ExercisePickerPresentationState.RequestFailure, 0 },
        { "offline-empty", false, false, null, ExercisePickerPresentationState.OfflineWithoutCache, 0 },
        { "offline-cache", false, true, null, ExercisePickerPresentationState.OfflineWithCache, 1 },
        { "failure-with-cache", true, true, null, ExercisePickerPresentationState.OfflineWithCache, 1 },
        { "no-filter-matches", true, false, BodyPart.Back, ExercisePickerPresentationState.NoFilterMatches, 0 }
    };

    [Theory]
    [MemberData(nameof(PickerCases))]
    public async Task Picker_exposes_honest_state(
        string scenario,
        bool isOnline,
        bool seedCache,
        BodyPart? bodyPart,
        ExercisePickerPresentationState expectedState,
        int expectedCount)
    {
        var exercise = ChestPressWithPerformance();
        if (seedCache) await _cache.ReplaceAllAsync([exercise], DateTimeOffset.UtcNow);
        IExerciseCatalogApi api = scenario switch
        {
            "authentication" => new ApiProblemCatalogApi(new MobileApiException(
                TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest,
                "Authentication is required.",
                isAuthenticationRequired: true)),
            "request-failure" or "failure-with-cache" => new FailingCatalogApi(),
            "timeout" => new TimeoutCatalogApi(),
            _ => new ImmediateCatalogApi([exercise])
        };
        var sut = new ExercisePickerViewModel(
            _cache, api, new StubConnectivity(isOnline), new FixedClock());

        await sut.LoadAsync(bodyPart);
        await sut.RefreshCompletion;

        Assert.Equal(expectedState, sut.PresentationState);
        Assert.Equal(expectedCount, sut.Exercises.Count);
        Assert.Equal(expectedState is ExercisePickerPresentationState.Results or ExercisePickerPresentationState.OfflineWithCache, sut.HasResults);
        Assert.Equal(expectedState == ExercisePickerPresentationState.InitialLoading, sut.ShowLoading);
        Assert.Equal(expectedState == ExercisePickerPresentationState.AuthenticationRequired, sut.ShowAuthenticationRequired);
        Assert.Equal(expectedState == ExercisePickerPresentationState.OfflineWithoutCache, sut.ShowOfflineEmpty);
        Assert.Equal(expectedState == ExercisePickerPresentationState.RequestFailure, sut.ShowRequestFailure);
        Assert.Equal(expectedState == ExercisePickerPresentationState.NoFilterMatches, sut.ShowNoMatches);
    }

    [Fact]
    public async Task Picker_is_initial_loading_before_its_first_cache_read_completes()
    {
        var dispatcher = new GatedUiDispatcher();
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi([ChestPressWithPerformance()]),
            new StubConnectivity(true),
            new FixedClock(),
            dispatcher: dispatcher);

        var loading = sut.LoadAsync();
        await dispatcher.Entered.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ExercisePickerPresentationState.InitialLoading, sut.PresentationState);
        Assert.True(sut.ShowLoading);
        dispatcher.Release();
        await loading;
        await sut.RefreshCompletion;
    }

    [Fact]
    public async Task Retry_command_recovers_request_failure_with_a_fresh_generation()
    {
        var api = new FailThenSucceedCatalogApi(ChestPressWithPerformance());
        var sut = new ExercisePickerViewModel(
            _cache, api, new StubConnectivity(true), new FixedClock());
        await sut.LoadAsync();
        await sut.RefreshCompletion;
        Assert.Equal(ExercisePickerPresentationState.RequestFailure, sut.PresentationState);

        await sut.RetryCommand.ExecuteAsync();

        Assert.Equal(ExercisePickerPresentationState.Results, sut.PresentationState);
        Assert.Single(sut.Exercises);
        Assert.Equal(2, api.RequestCount);
    }

    [Fact]
    public async Task Thai_request_failure_never_exposes_an_English_transport_message()
    {
        var text = WorkoutResources.ForCulture(
            System.Globalization.CultureInfo.GetCultureInfo("th-TH"));
        var sut = new ExercisePickerViewModel(
            _cache,
            new FailingCatalogApi(),
            new StubConnectivity(true),
            new FixedClock(),
            text: text);

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.Equal(text.LoadFailed, sut.StateMessage);
        Assert.DoesNotContain("catalog", sut.StateMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sign_in_command_uses_the_auth_entry_point()
    {
        var entryPoint = new RecordingAuthEntryPoint();
        var sut = new ExercisePickerViewModel(
            _cache,
            new ApiProblemCatalogApi(new MobileApiException(
                TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest,
                "Authentication is required.",
                isAuthenticationRequired: true)),
            new StubConnectivity(true),
            new FixedClock(),
            authEntryPoint: entryPoint);
        await sut.LoadAsync();
        await sut.RefreshCompletion;

        await sut.SignInCommand.ExecuteAsync();

        Assert.Equal(1, entryPoint.CallCount);
    }

    [Fact]
    public async Task Delayed_sign_in_command_uses_the_generation_that_produced_authentication_required()
    {
        var boundary = new AccountSessionBoundary();
        var authenticationRequiredGeneration = boundary.Capture();
        var entryPoint = new RecordingAuthEntryPoint();
        var sut = new ExercisePickerViewModel(
            _cache,
            new ApiProblemCatalogApi(new MobileApiException(
                TrackZ.Contracts.Errors.BusinessErrorCode.InvalidRequest,
                "Authentication is required.",
                isAuthenticationRequired: true)),
            new StubConnectivity(true),
            new FixedClock(),
            boundary: boundary,
            authEntryPoint: entryPoint);
        await sut.LoadAsync();
        await sut.RefreshCompletion;
        Assert.Equal(ExercisePickerPresentationState.AuthenticationRequired, sut.PresentationState);

        await boundary.ResetAsync(_ => Task.CompletedTask);
        var newAccountGeneration = boundary.Capture();
        await sut.SignInCommand.ExecuteAsync();

        Assert.NotEqual(authenticationRequiredGeneration, newAccountGeneration);
        Assert.Equal(0, entryPoint.UnfencedCallCount);
        Assert.Equal(authenticationRequiredGeneration, Assert.Single(entryPoint.ExpectedGenerations));
    }

    [Fact]
    public async Task Account_reset_during_refresh_prevents_stale_picker_state_mutation()
    {
        var boundary = new AccountSessionBoundary();
        var api = new GatedCatalogApi();
        var sut = new ExercisePickerViewModel(
            _cache, api, new StubConnectivity(true), new FixedClock(), boundary: boundary);
        await sut.LoadAsync();
        var oldRefresh = sut.RefreshCompletion;

        await boundary.ResetAsync(_ => Task.CompletedTask);
        api.Complete([ChestPressWithPerformance()]);
        await oldRefresh;

        Assert.Empty(sut.Exercises);
        Assert.Equal(ExercisePickerPresentationState.InitialLoading, sut.PresentationState);
        Assert.Null(sut.LastError);
    }

    [Fact]
    public async Task Offline_picker_displays_cached_last_and_pr_and_allows_multi_select()
    {
        var press = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([press], new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero));
        var sut = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.English);

        await sut.LoadAsync(BodyPart.Chest);
        sut.ToggleSelectionCommand.Execute(sut.Exercises[0]);

        Assert.Equal(70m, sut.Exercises[0].LastBestSet!.WeightKg);
        Assert.Equal(75m, sut.Exercises[0].AllTimeBest!.WeightKg);
        Assert.Equal("LAST  70 kg × 8", sut.Exercises[0].LastText);
        Assert.Equal("PR  75 kg × 5", sut.Exercises[0].PersonalRecordText);
        Assert.Single(sut.SelectedExerciseIds);
    }

    [Fact]
    public async Task Rendered_picker_item_owns_a_namescope_independent_selection_command()
    {
        await _cache.ReplaceAllAsync(
            [ChestPressWithPerformance()],
            new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero));
        var sut = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.English);
        await sut.LoadAsync(BodyPart.Chest);
        var item = Assert.Single(sut.Exercises);

        item.ToggleSelectionCommand.Execute(null);

        Assert.True(item.IsSelected);
        Assert.Equal([item.Id], sut.SelectedExerciseIds);
        Assert.Equal("Selected: 1", sut.SelectedCountText);
    }

    [Fact]
    public async Task Load_shows_cache_before_online_refresh_then_replaces_it_transactionally()
    {
        var cached = ChestPressWithPerformance();
        var refreshed = Summary(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Cable Fly", BodyPart.Chest);
        await _cache.ReplaceAllAsync([cached], new DateTimeOffset(2026, 8, 14, 1, 0, 0, TimeSpan.Zero));
        var api = new GatedCatalogApi();
        var sut = new ExercisePickerViewModel(_cache, api, new StubConnectivity(true), new FixedClock());

        await sut.LoadAsync(BodyPart.Chest);

        Assert.Equal("Bench Press", Assert.Single(sut.Exercises).Name);
        api.Complete([refreshed]);
        await sut.RefreshCompletion;

        Assert.Equal("Cable Fly", Assert.Single(sut.Exercises).Name);
        var persisted = await new ExerciseCache(_databasePath).GetAllAsync();
        Assert.Equal("Cable Fly", Assert.Single(persisted).Name);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 2, 3, 4, TimeSpan.Zero), persisted[0].LastSyncedAt);
    }

    [Fact]
    public async Task Search_and_body_part_filter_preserve_selection_by_exercise_id()
    {
        var chest = ChestPressWithPerformance();
        var back = Summary(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Lat Pulldown", BodyPart.Back);
        await _cache.ReplaceAllAsync([chest, back], DateTimeOffset.UtcNow);
        var sut = new ExercisePickerViewModel(_cache, new StubCatalogApi(), new StubConnectivity(false), new FixedClock());

        await sut.LoadAsync(null);
        sut.ToggleSelectionCommand.Execute(chest.Id);
        sut.SearchText = " lat ";

        Assert.Equal("Lat Pulldown", Assert.Single(sut.Exercises).Name);
        sut.SearchText = string.Empty;
        sut.SelectedBodyPart = BodyPart.Chest;
        Assert.True(Assert.Single(sut.Exercises).IsSelected);
        Assert.Equal(chest.Id, Assert.Single(sut.SelectedExerciseIds));
    }

    [Fact]
    public async Task Selection_order_is_explicit_and_reselect_moves_the_stable_id_to_the_end()
    {
        var first = ChestPressWithPerformance();
        var second = Summary(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Lat Pulldown", BodyPart.Back);
        await _cache.ReplaceAllAsync([first, second], DateTimeOffset.UtcNow);
        var sut = new ExercisePickerViewModel(
            _cache, new StubCatalogApi(), new StubConnectivity(false), new FixedClock());
        await sut.LoadAsync();

        sut.ToggleSelectionCommand.Execute(first.Id);
        sut.ToggleSelectionCommand.Execute(second.Id);
        sut.ToggleSelectionCommand.Execute(first.Id);
        sut.ToggleSelectionCommand.Execute(first.Id);

        Assert.Equal([second.Id, first.Id], sut.SelectedExerciseIds);
    }

    [Fact]
    public async Task Thai_picker_copy_filter_options_and_selected_count_come_from_resources()
    {
        var exercise = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([exercise], DateTimeOffset.UtcNow);
        var sut = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH")));
        await sut.LoadAsync();

        Assert.Equal("เลือกท่าออกกำลังกาย", sut.Text.ChooseExercises);
        Assert.Equal("ค้นหาท่าออกกำลังกาย", sut.Text.SearchExercises);
        Assert.Equal("ทุกส่วนของร่างกาย", sut.Text.AllBodyParts);
        Assert.Equal("ไม่พบท่าออกกำลังกายที่ตรงกับตัวกรอง", sut.Text.NoMatchingExercises);
        Assert.Equal("สร้างท่าเอง", sut.Text.CreateCustom);
        Assert.Equal("เสร็จสิ้น", sut.Text.Done);
        Assert.Equal("เข้าสู่ระบบ", sut.Text.SignIn);
        Assert.Equal("ลองอีกครั้ง", sut.Text.TryAgain);
        Assert.Equal("เลือกแล้ว: 0", sut.SelectedCountText);
        Assert.Equal(["ทั้งหมด", "หน้าอก", "หลัง", "ไหล่", "แขน", "ขา", "แกนกลางลำตัว"],
            sut.BodyPartOptions.Select(item => item.Label));

        sut.ToggleSelectionCommand.Execute(exercise.Id);

        Assert.Equal("เลือกแล้ว: 1", sut.SelectedCountText);
    }

    [Fact]
    public async Task Picker_card_presentation_localizes_performance_and_actions_in_English_and_Thai()
    {
        var weighted = ChestPressWithPerformance();
        var assisted = new ExerciseSummaryDto(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Assisted Pull-up",
            BodyPart.Back,
            TrackingMode.Assisted,
            null,
            DateTimeOffset.UtcNow,
            new PerformanceSetDto(null, 27.5m, 8),
            new PerformanceSetDto(null, 25m, 10),
            true);
        var bodyweight = new ExerciseSummaryDto(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Pull-up",
            BodyPart.Back,
            TrackingMode.Bodyweight,
            null,
            DateTimeOffset.UtcNow,
            new PerformanceSetDto(null, null, 1),
            new PerformanceSetDto(null, null, 8),
            false);
        await _cache.ReplaceAllAsync([weighted, assisted, bodyweight], DateTimeOffset.UtcNow);

        var english = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));
        await english.LoadAsync();

        Assert.Equal("LAST  70 kg × 8", english.Exercises.Single(item => item.Id == weighted.Id).LastText);
        Assert.Equal("PR  75 kg × 5", english.Exercises.Single(item => item.Id == weighted.Id).PersonalRecordText);
        Assert.Equal("LAST  27.5 kg assist × 8", english.Exercises.Single(item => item.Id == assisted.Id).LastText);
        Assert.Equal("Edit", english.Exercises.Single(item => item.Id == assisted.Id).EditLabel);
        Assert.Equal("LAST  1 rep", english.Exercises.Single(item => item.Id == bodyweight.Id).LastText);
        Assert.Equal("PR  8 reps", english.Exercises.Single(item => item.Id == bodyweight.Id).PersonalRecordText);

        var thai = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH")));
        await thai.LoadAsync();

        Assert.Equal("ครั้งก่อน  70 กก. × 8", thai.Exercises.Single(item => item.Id == weighted.Id).LastText);
        Assert.Equal("สถิติสูงสุด  75 กก. × 5", thai.Exercises.Single(item => item.Id == weighted.Id).PersonalRecordText);
        Assert.Equal("ครั้งก่อน  27.5 กก. ช่วย × 8", thai.Exercises.Single(item => item.Id == assisted.Id).LastText);
        Assert.Equal("แก้ไข", thai.Exercises.Single(item => item.Id == assisted.Id).EditLabel);
        Assert.Equal("ครั้งก่อน  1 ครั้ง", thai.Exercises.Single(item => item.Id == bodyweight.Id).LastText);
        Assert.Equal("สถิติสูงสุด  8 ครั้ง", thai.Exercises.Single(item => item.Id == bodyweight.Id).PersonalRecordText);
    }

    [Fact]
    public async Task Picker_performance_uses_persisted_unit_preserves_kg_precision_and_refreshes_existing_items()
    {
        var weightedId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var assistedId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        await _cache.ReplaceAllAsync([
            new ExerciseSummaryDto(
                weightedId, "Precise Press", BodyPart.Chest, TrackingMode.Weighted,
                null, DateTimeOffset.UtcNow,
                new PerformanceSetDto(70.125m, null, 8),
                new PerformanceSetDto(70.125m, null, 5),
                false),
            new ExerciseSummaryDto(
                assistedId, "Precise Pull-up", BodyPart.Back, TrackingMode.Assisted,
                null, DateTimeOffset.UtcNow,
                new PerformanceSetDto(null, 25.125m, 10),
                new PerformanceSetDto(null, 25.125m, 8),
                false)
        ], DateTimeOffset.UtcNow);
        var store = new MemoryWorkoutPreferenceStore();
        var preference = new WeightUnitPreference(store);
        var sut = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")),
            unitPreference: preference);
        await sut.LoadAsync();

        Assert.Equal("LAST  70.125 kg × 8", sut.Exercises.Single(item => item.Id == weightedId).LastText);
        Assert.Equal("LAST  25.125 kg assist × 10", sut.Exercises.Single(item => item.Id == assistedId).LastText);

        preference.Set(WeightDisplayUnit.Pounds);

        Assert.Equal("LAST  154.60 lb × 8", sut.Exercises.Single(item => item.Id == weightedId).LastText);
        Assert.Equal("LAST  55.39 lb assist × 10", sut.Exercises.Single(item => item.Id == assistedId).LastText);
        Assert.Equal(70.125m, sut.Exercises.Single(item => item.Id == weightedId).LastBestSet!.WeightKg);
        Assert.Equal(25.125m, sut.Exercises.Single(item => item.Id == assistedId).LastBestSet!.AssistedKg);

        var restored = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")),
            unitPreference: new WeightUnitPreference(store));
        await restored.LoadAsync();

        Assert.Equal("PR  154.60 lb × 5", restored.Exercises.Single(item => item.Id == weightedId).PersonalRecordText);
        Assert.Equal("PR  55.39 lb assist × 8", restored.Exercises.Single(item => item.Id == assistedId).PersonalRecordText);

        var thai = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH")),
            unitPreference: new WeightUnitPreference(store));
        await thai.LoadAsync();

        Assert.Equal("ครั้งก่อน  154.60 ปอนด์ × 8", thai.Exercises.Single(item => item.Id == weightedId).LastText);
        Assert.Equal("ครั้งก่อน  55.39 ปอนด์ ช่วย × 10", thai.Exercises.Single(item => item.Id == assistedId).LastText);
    }

    [Fact]
    public async Task Pending_custom_exercise_exposes_a_localized_sync_label_through_picker_presentation()
    {
        var id = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await _cache.QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), id, id, "Pending Press", BodyPart.Chest, TrackingMode.Weighted,
            null, "/local/original.png", "image/png", DateTimeOffset.UtcNow));
        var sut = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("th-TH")));

        await sut.LoadAsync();

        Assert.Equal("รอซิงค์", Assert.Single(sut.Exercises).SyncLabel);

        var english = new ExercisePickerViewModel(
            _cache,
            new StubCatalogApi(),
            new StubConnectivity(false),
            new FixedClock(),
            text: WorkoutResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));
        await english.LoadAsync();

        Assert.Equal("Pending Sync", Assert.Single(english.Exercises).SyncLabel);
    }

    [Fact]
    public async Task Failed_replacement_leaves_the_previous_catalog_intact()
    {
        var cached = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([cached], DateTimeOffset.UtcNow);
        var invalid = Summary(Guid.NewGuid(), " ", BodyPart.Chest);

        await Assert.ThrowsAsync<ArgumentException>(() => _cache.ReplaceAllAsync([invalid], DateTimeOffset.UtcNow));

        Assert.Equal(cached.Id, Assert.Single(await _cache.GetAllAsync()).Id);
    }

    [Fact]
    public async Task Catalog_http_client_follows_opaque_cursors_until_all_items_are_loaded()
    {
        var firstId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var secondId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var handler = new QueueHttpHandler(
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{firstId}}","name":"First","bodyPart":1,"trackingMode":1,"thumbnailUrl":null,"lastPerformedAt":null,"lastBestSet":null,"allTimeBest":null,"isCustom":false}],"nextCursor":"opaque.cursor"}"""),
            Json(HttpStatusCode.OK, $$"""{"items":[{"id":"{{secondId}}","name":"Second","bodyPart":2,"trackingMode":1,"thumbnailUrl":null,"lastPerformedAt":null,"lastBestSet":null,"allTimeBest":null,"isCustom":false}],"nextCursor":null}"""));
        var client = new TrackZExerciseApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") });

        var result = await client.GetAllAsync();

        Assert.Equal([firstId, secondId], result.Select(item => item.Id));
        Assert.Equal("/api/v1/exercises?pageSize=50", handler.Requests[0].PathAndQuery);
        Assert.Equal("/api/v1/exercises?pageSize=50&cursor=opaque.cursor", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task Background_network_failure_keeps_cache_and_exposes_stable_error_without_faulting_load()
    {
        var cached = ChestPressWithPerformance();
        await _cache.ReplaceAllAsync([cached], DateTimeOffset.UtcNow);
        var sut = new ExercisePickerViewModel(
            _cache,
            new FailingCatalogApi(),
            new StubConnectivity(true),
            new FixedClock());

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.Equal(cached.Id, Assert.Single(sut.Exercises).Id);
        Assert.Equal(TrackZ.Contracts.Errors.BusinessErrorCode.InternalServerError, sut.LastErrorCode);
    }

    [Fact]
    public async Task Refresh_does_not_overwrite_pending_local_thumbnail_or_label()
    {
        var id = Guid.Parse("77777777-7777-7777-7777-777777777777");
        await _cache.QueueAsync(new PendingCustomExercise(
            Guid.NewGuid(), id, id, "Pending Press", BodyPart.Chest, TrackingMode.Weighted,
            null, "/local/original.png", "image/png", DateTimeOffset.UtcNow));

        await _cache.ReplaceAllAsync(
            [Summary(id, "Pending Press", BodyPart.Chest)],
            new DateTimeOffset(2026, 8, 15, 4, 0, 0, TimeSpan.Zero));

        var cached = Assert.Single(await _cache.GetAllAsync());
        Assert.True(cached.IsPendingSync);
        Assert.Equal("/local/original.png", cached.ThumbnailUri);
    }

    [Fact]
    public async Task Api_http_handler_adds_the_current_bearer_token()
    {
        var terminal = new AuthorizationRecordingHandler();
        var handler = new BearerTokenHandler(new StubAccessTokenProvider(), new Uri("https://trackz.test")) { InnerHandler = terminal };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://trackz.test") };

        await client.GetAsync("/api/v1/exercises");

        Assert.Equal("Bearer", terminal.Authorization?.Scheme);
        Assert.Equal("access-token", terminal.Authorization?.Parameter);
    }

    [Fact]
    public async Task Online_refresh_caches_protected_thumbnail_to_a_local_path()
    {
        var remote = new ExerciseSummaryDto(
            Guid.NewGuid(), "Private Press", BodyPart.Chest, TrackingMode.Weighted,
            "/api/v1/media/exercise-images/private/thumbnail", null, null, null, true);
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi([remote]),
            new StubConnectivity(true),
            new FixedClock(),
            thumbnailCache: new StubThumbnailCache());

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.Equal("/local/private-thumbnail.jpg", Assert.Single(sut.Exercises).ThumbnailUri);
        Assert.Equal("/local/private-thumbnail.jpg", Assert.Single(await _cache.GetAllAsync()).ThumbnailUri);
    }

    [Fact]
    public async Task Failed_thumbnail_does_not_discard_refreshed_metadata()
    {
        var refreshed = Summary(
            Guid.NewGuid(), "New Metadata", BodyPart.Back,
            "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi([refreshed]),
            new StubConnectivity(true),
            new FixedClock(),
            thumbnailCache: new FailingThumbnailCache());

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        var cached = Assert.Single(await _cache.GetAllAsync());
        Assert.Equal("New Metadata", cached.Name);
        Assert.Null(cached.ThumbnailUri);
        Assert.Null(sut.LastErrorCode);
        Assert.Equal(ExercisePickerPresentationState.Results, sut.PresentationState);
        Assert.Equal(ExerciseArtworkState.Failed, Assert.Single(sut.Exercises).ArtworkState);
    }

    [Fact]
    public async Task Thumbnail_fill_uses_bounded_parallelism()
    {
        var exercises = Enumerable.Range(1, 12)
            .Select(index => Summary(
                Guid.NewGuid(), $"Exercise {index}", BodyPart.Chest,
                $"/api/v1/media/exercise-images/{Guid.NewGuid():D}/thumbnail"))
            .ToArray();
        var thumbnails = new ConcurrencyTrackingThumbnailCache();
        var sut = new ExercisePickerViewModel(
            _cache,
            new ImmediateCatalogApi(exercises),
            new StubConnectivity(true),
            new FixedClock(),
            thumbnailCache: thumbnails);

        await sut.LoadAsync();
        await sut.RefreshCompletion;

        Assert.InRange(thumbnails.MaximumConcurrency, 2, 4);
        Assert.Equal(12, (await _cache.GetAllAsync()).Count(item => item.ThumbnailUri == "/local/image.jpg"));
    }

    [Fact]
    public async Task Inline_ui_dispatcher_serializes_actions_and_releases_gate_after_throw()
    {
        var dispatcher = new InlineUiDispatcher();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximum = 0;

        var first = Task.Run(() => dispatcher.InvokeAsync(() =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximum, current);
            firstEntered.SetResult();
            releaseFirst.Task.GetAwaiter().GetResult();
            Interlocked.Decrement(ref active);
        }));

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = dispatcher.InvokeAsync(() =>
        {
            var current = Interlocked.Increment(ref active);
            InterlockedExtensions.Max(ref maximum, current);
            Interlocked.Decrement(ref active);
        });

        Assert.False(second.IsCompleted);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, maximum);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.InvokeAsync(() => throw new InvalidOperationException("expected")));

        var laterActionCount = 0;
        await dispatcher.InvokeAsync(() => laterActionCount++);
        Assert.Equal(1, laterActionCount);
    }

    [Theory]
    [InlineData("//evil.example/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("/%2f%2fevil.example/x")]
    [InlineData("/\\evil.example/x")]
    [InlineData("https://trackz.test/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("http://trackz.test/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("https://trackz.test:444/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("https://evil.example/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail")]
    [InlineData("/api/v1/media/exercise-images/not-a-guid/thumbnail")]
    [InlineData("/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/master")]
    public async Task Thumbnail_cache_rejects_noncanonical_routes_without_sending_request(string route)
    {
        var handler = new NeverCalledHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(handler) { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(new NeverCalledHandler()),
                new Uri("https://media.trackz.test"),
                directory,
                new FixedClock(),
                new AccountSessionBoundary());

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.CacheAsync(route));
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Thumbnail_cache_gets_authorization_then_downloads_without_forwarding_the_api_bearer()
    {
        var clock = new FixedClock();
        var signedUrl = SignedThumbnailUrl(clock.UtcNow.AddMinutes(1));
        var authorization = new AuthorizationResponseHandler(signedUrl, clock.UtcNow.AddMinutes(1));
        var media = new ImageResponseHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var apiClient = new HttpClient(authorization) { BaseAddress = new Uri("https://api.trackz.test") };
            apiClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "private-api-token");
            var cache = new AuthenticatedExerciseThumbnailCache(
                apiClient,
                new HttpClient(media),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                new AccountSessionBoundary());

            var local = await cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");

            Assert.Equal(1, authorization.CallCount);
            Assert.Equal("Bearer private-api-token", authorization.Authorization);
            Assert.Equal(1, media.CallCount);
            Assert.Null(media.Authorization);
            Assert.EndsWith(".jpg", local, StringComparison.Ordinal);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(local!));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("//evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test.evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test@evil.example/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test:444/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test/media/v1/exercise-images/88888888-8888-8888-8888-888888888888/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test/media/v1/%2e%2e/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("https://media.trackz.test/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail?expires=1786759444&signature=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa&next=https://evil.example")]
    public async Task Thumbnail_cache_rejects_off_origin_or_confused_signed_urls_without_contacting_them(string signedUrl)
    {
        var clock = new FixedClock();
        var media = new NeverCalledHandler();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(new AuthorizationResponseHandler(signedUrl, DateTimeOffset.FromUnixTimeSeconds(1786759444)))
                { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(media),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                new AccountSessionBoundary());

            await Assert.ThrowsAsync<InvalidDataException>(() => cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail"));

            Assert.Equal(0, media.CallCount);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Expired_signed_download_reauthorizes_once_then_caches_the_bytes()
    {
        var clock = new FixedClock();
        var expiry = clock.UtcNow.AddMinutes(1);
        var signedUrl = SignedThumbnailUrl(expiry);
        var authorizations = new QueueHttpHandler(
            SignedAccessResponse(signedUrl, expiry),
            SignedAccessResponse(signedUrl, expiry));
        var media = new QueueHttpHandler(
            new HttpResponseMessage(HttpStatusCode.Gone),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = ImageResponseHandler.ImageContent("image/png") });
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(authorizations) { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(media),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                new AccountSessionBoundary());

            var local = await cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");

            Assert.Equal(2, authorizations.Requests.Count);
            Assert.Equal(2, media.Requests.Count);
            Assert.EndsWith(".png", local, StringComparison.Ordinal);
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(local!));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string SignedThumbnailUrl(DateTimeOffset expiresAt) =>
        $"https://media.trackz.test/media/v1/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail" +
        $"?expires={expiresAt.ToUnixTimeSeconds()}&signature={new string('a', 43)}";

    private static HttpResponseMessage SignedAccessResponse(string url, DateTimeOffset expiresAt) =>
        Json(HttpStatusCode.OK, $$"""{"url":"{{url}}","expiresAt":"{{expiresAt:O}}"}""");

    [Fact]
    public async Task Thumbnail_response_from_reset_generation_is_never_promoted_to_account_cache()
    {
        var handler = new DelayedImageResponseHandler();
        var clock = new FixedClock();
        var directory = Path.Combine(Path.GetTempPath(), $"trackz-thumbnails-{Guid.NewGuid():N}");
        var boundary = new AccountSessionBoundary();
        try
        {
            var cache = new AuthenticatedExerciseThumbnailCache(
                new HttpClient(new AuthorizationResponseHandler(
                    SignedThumbnailUrl(clock.UtcNow.AddMinutes(1)),
                    clock.UtcNow.AddMinutes(1))) { BaseAddress = new Uri("https://api.trackz.test") },
                new HttpClient(handler),
                new Uri("https://media.trackz.test"),
                directory,
                clock,
                boundary);
            var caching = cache.CacheAsync(
                "/api/v1/media/exercise-images/99999999-9999-9999-9999-999999999999/thumbnail");
            await handler.Entered;

            await boundary.ResetAsync(cache.ClearAsync);
            handler.Release();
            var local = await caching;

            Assert.Null(local);
            Assert.Empty(Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : []);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Bearer_handler_never_sends_token_to_off_origin_absolute_request()
    {
        var terminal = new AuthorizationRecordingHandler();
        var handler = new BearerTokenHandler(new StubAccessTokenProvider(), new Uri("https://trackz.test")) { InnerHandler = terminal };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://evil.example/x");

        Assert.Null(terminal.Authorization);
    }

    private static ExerciseSummaryDto ChestPressWithPerformance() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "Bench Press",
        BodyPart.Chest,
        TrackingMode.Weighted,
        "/images/bench-thumbnail",
        new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
        new PerformanceSetDto(70m, null, 8),
        new PerformanceSetDto(75m, null, 5),
        false);

    private static ExerciseSummaryDto Summary(Guid id, string name, BodyPart bodyPart, string? thumbnailUrl = null) => new(
        id, name, bodyPart, TrackingMode.Weighted, thumbnailUrl, null, null, null, false);

    private sealed class StubConnectivity(bool isOnline) : IConnectivityService
    {
        public bool IsOnline { get; } = isOnline;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class MemoryWorkoutPreferenceStore : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class StubCatalogApi : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExerciseSummaryDto>>([]);
    }

    private sealed class GatedCatalogApi : IExerciseCatalogApi
    {
        private readonly TaskCompletionSource<IReadOnlyList<ExerciseSummaryDto>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            _completion.Task.WaitAsync(cancellationToken);

        public void Complete(IReadOnlyList<ExerciseSummaryDto> exercises) => _completion.SetResult(exercises);
    }

    private sealed class FailingCatalogApi : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("offline");
    }

    private sealed class ApiProblemCatalogApi(MobileApiException exception) : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class TimeoutCatalogApi : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new TimeoutException("catalog timed out");
    }

    private sealed class FailThenSucceedCatalogApi(ExerciseSummaryDto exercise) : IExerciseCatalogApi
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _requestCount) == 1) throw new HttpRequestException("offline");
            return Task.FromResult<IReadOnlyList<ExerciseSummaryDto>>([exercise]);
        }
    }

    private sealed class ImmediateCatalogApi(IReadOnlyList<ExerciseSummaryDto> exercises) : IExerciseCatalogApi
    {
        public Task<IReadOnlyList<ExerciseSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(exercises);
    }

    private sealed class StubThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(thumbnailUri is null ? null : "/local/private-thumbnail.jpg");
    }

    private sealed class FailingThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default) =>
            throw new IOException("thumbnail unavailable");
    }

    private sealed class ConcurrencyTrackingThumbnailCache : IExerciseThumbnailCache
    {
        private int _current;
        private int _maximum;
        public int MaximumConcurrency => _maximum;

        public async Task<string?> CacheAsync(string? thumbnailUri, CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _current);
            InterlockedExtensions.Max(ref _maximum, current);
            try
            {
                await Task.Delay(20, cancellationToken);
                return "/local/image.jpg";
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref location);
                if (observed >= value) return;
            }
            while (Interlocked.CompareExchange(ref location, value, observed) != observed);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 15, 2, 3, 4, TimeSpan.Zero);
    }

    private sealed class GatedUiDispatcher : IUiDispatcher
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public async Task InvokeAsync(Action action)
        {
            _entered.TrySetResult();
            await _release.Task;
            action();
        }
        public void Release() => _release.TrySetResult();
    }

    private sealed class RecordingAuthEntryPoint : IAuthEntryPoint
    {
        public int CallCount { get; private set; }
        public int UnfencedCallCount { get; private set; }
        public List<AccountSessionGeneration> ExpectedGenerations { get; } = [];
        public Task RequireSignInAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            UnfencedCallCount++;
            return Task.CompletedTask;
        }

        public Task RequireSignInAsync(
            AccountSessionGeneration expectedGeneration,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            ExpectedGenerations.Add(expectedGeneration);
            return Task.CompletedTask;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class StubAccessTokenProvider : IAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("access-token");
    }

    private sealed class AuthorizationRecordingHandler : HttpMessageHandler
    {
        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class ImageResponseHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ImageContent()
            });
        }

        public static ByteArrayContent ImageContent(string contentType = "image/jpeg")
        {
            var content = new ByteArrayContent([1, 2, 3]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return content;
        }
    }

    private sealed class AuthorizationResponseHandler(string url, DateTimeOffset expiresAt) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(Json(
                HttpStatusCode.OK,
                $$"""{"url":"{{url}}","expiresAt":"{{expiresAt:O}}"}"""));
        }
    }

    private sealed class DelayedImageResponseHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = ImageResponseHandler.ImageContent()
            };
        }
    }
}
