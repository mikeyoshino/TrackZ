using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Exercises.Services;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ExercisePickerInteractionTests : IDisposable
{
    private readonly CultureSnapshot _culture = CultureSnapshot.Capture();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"trackz-picker-interaction-{Guid.NewGuid():N}");
    private readonly IDispatcherProvider _originalDispatcher = DispatcherProvider.Current;
    private readonly MauiApp _app;
    private readonly RecordingExercisePickerNavigator _navigator = new();
    private readonly RecordingExercisePickerWarning _warning = new();
    private readonly RecordingLocalExerciseImagePicker _imagePicker = new();
    private readonly RecordingLocalExerciseImageCapture _imageCapture = new();
    private readonly RecordingExerciseOptionPicker _optionPicker = new();
    private readonly TestFileSystem _fileSystem;

    public ExercisePickerInteractionTests()
    {
        Directory.CreateDirectory(_root);
        _fileSystem = new TestFileSystem(_root);
        DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
        _app = MauiProgram.CreateMauiApp(services =>
        {
            services.AddSingleton(new ExerciseCache(Path.Combine(_root, "exercises.db")));
            services.AddSingleton(new ExerciseHistoryCache(Path.Combine(_root, "history.db")));
            services.AddSingleton(new TrackZLocalDatabase(Path.Combine(_root, "workouts.db")));
            services.AddSingleton(new ProgressSnapshotCache(Path.Combine(_root, "progress.json")));
            services.AddSingleton<IExerciseThumbnailCache, NullThumbnailCache>();
            services.AddSingleton<IConnectivityService, OfflineConnectivity>();
            services.AddSingleton<IUiDispatcher, InlineUiDispatcher>();
            services.AddSingleton<IWorkoutPreferenceStore, MemoryPreferences>();
            services.AddSingleton<IReduceMotionPreference>(new FixedReduceMotionPreference(false));
            services.AddSingleton<IExercisePickerNavigator>(_navigator);
            services.AddSingleton<IExercisePickerWarning>(_warning);
            services.AddSingleton<ILocalExerciseImagePicker>(_imagePicker);
            services.AddSingleton<ILocalExerciseImageCapture>(_imageCapture);
            services.AddSingleton<IExerciseOptionPicker>(_optionPicker);
            services.AddSingleton<IFileSystem>(_fileSystem);
        }, new FixedLanguageStore(AppLanguage.English));
        _ = _app.Services.GetRequiredService<App>();
    }

    private sealed class FixedReduceMotionPreference(bool isEnabled) : IReduceMotionPreference
    {
        public bool IsEnabled { get; } = isEnabled;
    }

    [Fact]
    public void Filter_trigger_opens_a_dismissible_multi_select_panel()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        page.Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, 393, 852));
        var filter = page.FindByName<Button>("BodyPartFilterBand");
        var root = page.FindByName<Grid>("RootLayout");
        var backgroundChildren = root.Children.OfType<VisualElement>().ToArray();
        filter.SendClicked();
        Assert.Equal(backgroundChildren.Length + 1, root.Children.Count);
        Assert.All(backgroundChildren, child =>
        {
            Assert.True(child.IsEnabled);
            Assert.True(child.InputTransparent);
        });
        var overlay = Assert.IsType<Grid>(root.Children[^1]);
        Assert.False(overlay.InputTransparent);
        Assert.Equal(root.RowDefinitions.Count, Grid.GetRowSpan(overlay));
    }

    [Fact]
    public async Task Finish_is_disabled_until_at_least_one_exercise_is_selected()
    {
        var exerciseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        await _app.Services.GetRequiredService<ExerciseCache>().ReplaceAllAsync([
            new ExerciseSummaryDto(
                exerciseId, "Bench Press", BodyPart.Chest, TrackingMode.Weighted,
                null, null, null, null, false)
        ], DateTimeOffset.UtcNow);
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var picker = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        await picker.LoadAsync(BodyPart.Chest);
        var done = page.FindByName<Button>("DoneButton");

        Assert.False(done.IsEnabled);

        picker.Exercises.Single().ToggleSelectionCommand.Execute(null);

        Assert.True(done.IsEnabled);
    }

    [Fact]
    public async Task Opening_a_different_body_category_discards_an_unconfirmed_selection()
    {
        var backId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var chestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        await _app.Services.GetRequiredService<ExerciseCache>().ReplaceAllAsync([
            new ExerciseSummaryDto(
                backId, "Lat Pulldown", BodyPart.Back, TrackingMode.Weighted,
                null, null, null, null, false),
            new ExerciseSummaryDto(
                chestId, "Bench Press", BodyPart.Chest, TrackingMode.Weighted,
                null, null, null, null, false)
        ], DateTimeOffset.UtcNow);
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var picker = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        await picker.LoadAsync(BodyPart.Back);
        picker.Exercises.Single().ToggleSelectionCommand.Execute(null);

        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["bodyPart"] = ((int)BodyPart.Chest).ToString(CultureInfo.InvariantCulture)
        });

        Assert.Empty(picker.SelectedExerciseIds);
        Assert.Equal("Selected 0 exercises", picker.SelectedCountText);
        Assert.False(picker.CanCompleteSelection);
    }

    [Fact]
    public async Task Returning_from_technique_to_the_same_category_keeps_the_unconfirmed_selection()
    {
        var exerciseId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        await _app.Services.GetRequiredService<ExerciseCache>().ReplaceAllAsync([
            new ExerciseSummaryDto(
                exerciseId, "Lat Pulldown", BodyPart.Back, TrackingMode.Weighted,
                null, null, null, null, false)
        ], DateTimeOffset.UtcNow);
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var picker = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        var route = new Dictionary<string, object>
        {
            ["bodyPart"] = ((int)BodyPart.Back).ToString(CultureInfo.InvariantCulture)
        };
        page.ApplyQueryAttributes(route);
        await picker.LoadAsync(BodyPart.Back);
        picker.Exercises.Single().ToggleSelectionCommand.Execute(null);

        // Shell can re-apply the parent page query when the technique page is popped.
        page.ApplyQueryAttributes(route);

        Assert.Equal([exerciseId], picker.SelectedExerciseIds);
        Assert.True(picker.CanCompleteSelection);
    }

    [Fact]
    public void Selecting_a_body_part_from_the_native_picker_updates_the_filter_label()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var viewModel = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        var filter = page.FindByName<Button>("BodyPartFilterBand");
        var option = viewModel.BodyPartOptions
            .Single(item => item.Value == BodyPart.Legs);

        viewModel.SelectedBodyPartOption = option;

        Assert.True(option.IsSelected);
        Assert.Equal(BodyPart.Legs, viewModel.SelectedBodyPart);
        Assert.Equal("Legs", filter.Text);
    }

    [Fact]
    public void Body_part_filter_keeps_a_full_native_touch_target()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var filter = page.FindByName<Button>("BodyPartFilterBand");
        Assert.True(filter.MinimumHeightRequest >= 44 || ((Grid)filter.Parent).RowDefinitions[2].Height.Value >= 44);
    }

    [Fact]
    public void Returning_to_all_updates_the_single_filter_label()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var viewModel = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        var filter = page.FindByName<Button>("BodyPartFilterBand");
        var options = viewModel.BodyPartOptions;
        var all = options.Single(option => option.Value is null);
        var legs = options.Single(option => option.Value == BodyPart.Legs);

        viewModel.SelectedBodyPartOption = legs;
        viewModel.SelectedBodyPartOption = all;

        Assert.False(legs.IsSelected);
        Assert.Null(viewModel.SelectedBodyPart);
        Assert.Equal("All muscles · All equipment", filter.Text);
    }

    [Fact]
    public void Custom_exercise_uses_the_trimmed_suggested_name_from_navigation()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        var nameField = Assert.Single(Descendants(page).OfType<Entry>());

        page.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["suggestedName"] = "%20%20Smith%20%26%20Machine%20Row%20%20"
        });

        Assert.Equal("Smith & Machine Row", viewModel.Name);
        Assert.Equal("Smith & Machine Row", nameField.Text);
    }

    [Fact]
    public void Custom_exercise_offers_camera_and_photo_library_actions()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        var imageActions = Descendants(page).OfType<Button>().ToArray();

        Assert.Contains(imageActions, button => button.Text == viewModel.Text.CaptureExerciseImage);
        Assert.Contains(imageActions, button => button.Text == viewModel.Text.ChooseExerciseImage);
        Assert.Empty(Descendants(page).OfType<CollectionView>());
    }

    [Fact]
    public void Custom_exercise_add_image_action_opens_the_local_picker_immediately()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        var imageAction = Descendants(page)
            .OfType<Button>()
            .Single(button => button.Text == viewModel.Text.ChooseExerciseImage);

        imageAction.SendClicked();

        Assert.Equal(1, _imagePicker.PickCount);
        Assert.Equal(viewModel.Text.ChooseExerciseImage, _imagePicker.LastTitle);
    }

    [Fact]
    public void Custom_exercise_camera_action_opens_the_camera_immediately()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        var cameraAction = Descendants(page)
            .OfType<Button>()
            .Single(button => button.Text == viewModel.Text.CaptureExerciseImage);

        cameraAction.SendClicked();

        Assert.Equal(1, _imageCapture.CaptureCount);
    }

    [Fact]
    public void Custom_exercise_form_uses_equal_fields_and_a_large_equipment_photo_card()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var content = Assert.IsType<VerticalStackLayout>(page.FindByName("CustomExerciseForm"));
        var nameField = Assert.IsType<Border>(page.FindByName("CustomExerciseNameField"));
        var bodyPartField = Assert.IsType<Border>(page.FindByName("CustomExerciseBodyPartField"));
        var trackingField = Assert.IsType<Border>(page.FindByName("CustomExerciseTrackingField"));
        var photoCard = Assert.IsType<Border>(page.FindByName("CustomExercisePhotoCard"));
        var preview = Assert.IsType<Image>(page.FindByName("CustomExerciseImagePreview"));

        Assert.Equal(12, content.Spacing);
        Assert.Equal(nameField.MinimumHeightRequest, bodyPartField.MinimumHeightRequest);
        Assert.Equal(nameField.MinimumHeightRequest, trackingField.MinimumHeightRequest);
        Assert.Equal(184, photoCard.HeightRequest);
        Assert.Equal(Aspect.AspectFill, preview.Aspect);
    }

    [Fact]
    public void Custom_exercise_uses_app_owned_selection_triggers_instead_of_native_pickers()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();

        Assert.Empty(Descendants(page).OfType<Picker>());
        Assert.NotNull(page.FindByName<Button>("CustomExerciseBodyPartButton"));
        Assert.NotNull(page.FindByName<Button>("CustomExerciseTrackingButton"));
    }

    [Fact]
    public void Custom_exercise_selection_triggers_remain_in_ios_hit_testing()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var bodyPart = page.FindByName<Button>("CustomExerciseBodyPartButton");
        var tracking = page.FindByName<Button>("CustomExerciseTrackingButton");

        Assert.True(bodyPart.Opacity > 0.01);
        Assert.True(tracking.Opacity > 0.01);
        Assert.False(bodyPart.InputTransparent);
        Assert.False(tracking.InputTransparent);
    }

    [Fact]
    public async Task Custom_exercise_selection_triggers_update_the_form_value()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        _optionPicker.BodyPartResult = viewModel.BodyPartOptions.Single(option => option.Value == BodyPart.Legs);
        _optionPicker.TrackingModeResult = viewModel.TrackingModeOptions.Single(option => option.Value == TrackingMode.Bodyweight);

        page.FindByName<Button>("CustomExerciseBodyPartButton").SendClicked();
        page.FindByName<Button>("CustomExerciseTrackingButton").SendClicked();
        await _optionPicker.SelectionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(BodyPart.Legs, viewModel.BodyPart);
        Assert.Equal(TrackingMode.Bodyweight, viewModel.TrackingMode);
    }

    [Fact]
    public async Task Body_part_option_sheet_uses_large_detent_so_every_choice_is_reachable()
    {
        var presenter = new RecordingNativeSheetPresenter();
        var picker = new MauiExerciseOptionPicker(presenter);
        var viewModel = _app.Services.GetRequiredService<CustomExerciseViewModel>();

        var pending = picker.PickBodyPartAsync(
            viewModel.Text.BodyPart,
            viewModel.BodyPartOptions,
            selected: null);
        var sheet = Assert.IsType<ExerciseOptionSheetPage>(presenter.Page);
        var detent = presenter.Detent;
        await sheet.CancelAsync();
        await pending;

        Assert.Equal(NativeSheetDetent.Large, detent);
    }

    [Fact]
    public void Custom_exercise_hides_the_sticky_save_action_while_a_picker_has_focus()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var actions = Assert.IsType<Border>(page.FindByName("CustomExerciseActions"));

        Assert.True(actions.IsVisible);

        page.SetFormInputFocused(true);
        Assert.False(actions.IsVisible);

        page.SetFormInputFocused(false);
        Assert.True(actions.IsVisible);
    }

    [Fact]
    public void Custom_exercise_is_a_focused_subflow_without_the_app_tab_bar()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();

        Assert.False(Shell.GetTabBarIsVisible(page));
    }

    [Fact]
    public void Create_custom_passes_the_trimmed_search_name_to_navigation()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var viewModel = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        viewModel.SearchText = "  Smith Machine Row  ";
        var createButton = Descendants(page)
            .OfType<Button>()
            .Single(button => button.Text == viewModel.CreateLabel);

        createButton.SendClicked();

        Assert.Equal("Smith Machine Row", Assert.Single(_navigator.SuggestedNames));
    }

    [Fact]
    public async Task App_owned_filter_tap_updates_results_and_selected_emphasis_including_all()
    {
        var chestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var backId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await _app.Services.GetRequiredService<ExerciseCache>().ReplaceAllAsync([
            new ExerciseSummaryDto(
                chestId, "Bench Press", BodyPart.Chest, TrackingMode.Weighted,
                null, null, null, null, false),
            new ExerciseSummaryDto(
                backId, "Lat Pulldown", BodyPart.Back, TrackingMode.Weighted,
                null, null, null, null, false)
        ], DateTimeOffset.UtcNow);
        var picker = _app.Services.GetRequiredService<ExercisePickerViewModel>();
        await picker.LoadAsync();
        var all = picker.BodyPartOptions.Single(option => option.Value is null);
        var back = picker.BodyPartOptions.Single(option => option.Value == BodyPart.Back);

        back.SelectCommand.Execute(null);

        Assert.Equal(BodyPart.Back, picker.SelectedBodyPart);
        Assert.True(back.IsSelected);
        Assert.False(all.IsSelected);
        Assert.Equal(backId, Assert.Single(picker.Exercises).Id);

        all.SelectCommand.Execute(null);

        Assert.Null(picker.SelectedBodyPart);
        Assert.True(all.IsSelected);
        Assert.Equal([chestId, backId], picker.Exercises.Select(item => item.Id).Order());
    }

    [Fact]
    public async Task Done_with_no_exercises_warns_without_committing_or_leaving_the_picker()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var completionCount = 0;
        page.SelectionCompleted += _ =>
        {
            completionCount++;
            return Task.CompletedTask;
        };

        await page.DoneCommand.ExecuteAsync();

        var shown = Assert.Single(_warning.Shown);
        Assert.Equal("No exercises selected", shown.Title);
        Assert.Equal("Choose at least 1 exercise before creating your workout.", shown.Message);
        Assert.Equal("OK", shown.Dismiss);
        Assert.Equal(0, completionCount);
        Assert.Equal(0, _navigator.ReturnCount);
    }

    [Fact]
    public async Task Done_waits_for_selection_commit_and_ignores_double_taps_before_returning_once()
    {
        var exerciseId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await _app.Services.GetRequiredService<ExerciseCache>().ReplaceAllAsync([
            new ExerciseSummaryDto(
                exerciseId, "Cable Fly", BodyPart.Chest, TrackingMode.Weighted,
                null, null, null, null, false)
        ], DateTimeOffset.UtcNow);
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var picker = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        await picker.LoadAsync(BodyPart.Chest);
        picker.Exercises.Single().ToggleSelectionCommand.Execute(null);
        var commitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyCollection<Guid>? committed = null;
        page.SelectionCompleted += async selected =>
        {
            committed = selected;
            commitStarted.TrySetResult();
            await allowCommit.Task;
        };

        Assert.Same(page.DoneCommand, page.FindByName<Button>("DoneButton").Command);
        var firstTap = page.DoneCommand.ExecuteAsync();
        await commitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await page.DoneCommand.ExecuteAsync();

        Assert.Equal(0, _navigator.ReturnCount);
        allowCommit.TrySetResult();
        await firstTap.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([exerciseId], committed);
        Assert.Equal(1, _navigator.ReturnCount);
        Assert.Empty(picker.SelectedExerciseIds);
    }

    [Theory]
    [InlineData(true, "pop")]
    [InlineData(false, "pop", "active-workout")]
    public async Task Return_navigation_closes_the_picker_then_reuses_or_opens_the_workout(
        bool workoutIsPrevious,
        params string[] expectedTransitions)
    {
        var host = new RecordingExercisePickerNavigationHost(workoutIsPrevious);
        var navigator = new MauiExercisePickerNavigator(host);

        await navigator.ReturnToWorkoutAsync();

        Assert.Equal(expectedTransitions, host.Transitions);
    }

    [Fact]
    public async Task Custom_exercise_navigation_encodes_the_suggested_name()
    {
        var host = new RecordingExercisePickerNavigationHost(workoutIsPrevious: false);
        var navigator = new MauiExercisePickerNavigator(host);

        await navigator.OpenCustomExerciseAsync("Smith & Machine Row");

        Assert.Equal(
            ["custom:CustomExercisePage?suggestedName=Smith%20%26%20Machine%20Row"],
            host.Transitions);
    }

    [Fact]
    public async Task Technique_navigation_opens_the_selected_exercise_detail()
    {
        var exerciseId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var host = new RecordingExercisePickerNavigationHost(workoutIsPrevious: true);
        var navigator = new MauiExercisePickerNavigator(host);

        await navigator.OpenTechniqueAsync(exerciseId);

        Assert.Equal(
            [$"technique:ExerciseTechniquePage?exerciseId={exerciseId:D}"],
            host.Transitions);
    }

    public void Dispose()
    {
        try
        {
            _app.Dispose();
            DispatcherProvider.SetCurrent(_originalDispatcher);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
        finally
        {
            _culture.Restore();
        }
    }

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

    private sealed class NullThumbnailCache : IExerciseThumbnailCache
    {
        public Task<string?> CacheAsync(
            string? thumbnailUri,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class OfflineConnectivity : IConnectivityService
    {
        public bool IsOnline => false;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class RecordingLocalExerciseImagePicker : ILocalExerciseImagePicker
    {
        public int PickCount { get; private set; }
        public string? LastTitle { get; private set; }

        public Task<LocalExerciseImageSelection?> PickAsync(
            string pickerTitle,
            CancellationToken cancellationToken = default)
        {
            PickCount++;
            LastTitle = pickerTitle;
            return Task.FromResult<LocalExerciseImageSelection?>(null);
        }
    }

    private sealed class RecordingLocalExerciseImageCapture : ILocalExerciseImageCapture
    {
        public int CaptureCount { get; private set; }

        public Task<LocalExerciseImageSelection?> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult<LocalExerciseImageSelection?>(null);
        }
    }

    private sealed class RecordingExerciseOptionPicker : IExerciseOptionPicker
    {
        private int _selectionCount;

        public LocalizedBodyPartOption? BodyPartResult { get; set; }
        public LocalizedTrackingModeOption? TrackingModeResult { get; set; }
        public TaskCompletionSource SelectionCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LocalizedBodyPartOption?> PickBodyPartAsync(
            string title,
            IReadOnlyList<LocalizedBodyPartOption> options,
            LocalizedBodyPartOption? selected,
            CancellationToken cancellationToken = default)
        {
            CompleteSelection();
            return Task.FromResult(BodyPartResult);
        }

        public Task<LocalizedTrackingModeOption?> PickTrackingModeAsync(
            string title,
            IReadOnlyList<LocalizedTrackingModeOption> options,
            LocalizedTrackingModeOption? selected,
            CancellationToken cancellationToken = default)
        {
            CompleteSelection();
            return Task.FromResult(TrackingModeResult);
        }

        private void CompleteSelection()
        {
            if (Interlocked.Increment(ref _selectionCount) == 2)
                SelectionCompleted.TrySetResult();
        }
    }

    private sealed class RecordingNativeSheetPresenter : INativeSheetPresenter
    {
        public ContentPage? Page { get; private set; }
        public NativeSheetDetent? Detent { get; private set; }

        public Task ShowAsync(
            ContentPage page,
            NativeSheetDetent detent,
            CancellationToken cancellationToken = default)
        {
            Page = page;
            Detent = detent;
            return Task.CompletedTask;
        }

        public Task DismissAsync(
            ContentPage page,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestFileSystem(string root) : IFileSystem
    {
        public string CacheDirectory => Path.Combine(root, "cache");
        public string AppDataDirectory => root;

        public Task<Stream> OpenAppPackageFileAsync(string filename) =>
            throw new NotSupportedException();

        public Task<bool> AppPackageFileExistsAsync(string filename) =>
            Task.FromResult(false);
    }

    private sealed class MemoryPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class FixedLanguageStore(AppLanguage language) : IAppLanguageStore
    {
        public AppLanguage Read() => language;
        public void Write(AppLanguage value) => language = value;
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

    private sealed class RecordingExercisePickerNavigator : IExercisePickerNavigator
    {
        public int ReturnCount { get; private set; }
        public List<string> SuggestedNames { get; } = [];
        public List<Guid> TechniqueExerciseIds { get; } = [];

        public Task OpenCustomExerciseAsync(
            string suggestedName,
            CancellationToken cancellationToken = default)
        {
            SuggestedNames.Add(suggestedName);
            return Task.CompletedTask;
        }

        public Task ReturnToWorkoutAsync(CancellationToken cancellationToken = default)
        {
            ReturnCount++;
            return Task.CompletedTask;
        }

        public Task OpenTechniqueAsync(Guid exerciseId, CancellationToken cancellationToken = default)
        {
            TechniqueExerciseIds.Add(exerciseId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExercisePickerWarning : IExercisePickerWarning
    {
        public List<(string Title, string Message, string Dismiss)> Shown { get; } = [];

        public Task ShowAsync(
            Page page,
            string title,
            string message,
            string dismiss,
            CancellationToken cancellationToken = default)
        {
            Shown.Add((title, message, dismiss));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingExercisePickerNavigationHost(bool workoutIsPrevious)
        : IExercisePickerNavigationHost
    {
        public bool IsWorkoutImmediatelyBeforePicker => workoutIsPrevious;
        public List<string> Transitions { get; } = [];

        public Task PopPickerAsync(CancellationToken cancellationToken)
        {
            Transitions.Add("pop");
            return Task.CompletedTask;
        }

        public Task OpenWorkoutAsync(CancellationToken cancellationToken)
        {
            Assert.Equal(["pop"], Transitions);
            Transitions.Add("active-workout");
            return Task.CompletedTask;
        }

        public Task OpenCustomExerciseAsync(string route, CancellationToken cancellationToken)
        {
            Transitions.Add($"custom:{route}");
            return Task.CompletedTask;
        }

        public Task OpenTechniqueAsync(string route, CancellationToken cancellationToken)
        {
            Transitions.Add($"technique:{route}");
            return Task.CompletedTask;
        }
    }
}
