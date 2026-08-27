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
            services.AddSingleton<IExercisePickerNavigator>(_navigator);
            services.AddSingleton<IExercisePickerWarning>(_warning);
            services.AddSingleton<ILocalExerciseImagePicker>(_imagePicker);
            services.AddSingleton<IFileSystem>(_fileSystem);
        }, new FixedLanguageStore(AppLanguage.English));
        _ = _app.Services.GetRequiredService<App>();
    }

    [Fact]
    public void Body_part_filters_use_app_owned_taps_instead_of_native_cell_selection()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var filters = Descendants(page)
            .OfType<CollectionView>()
            .Single(view => view.ItemsSource is IEnumerable<BodyPartFilterOption>);
        var options = Assert.IsAssignableFrom<IEnumerable<BodyPartFilterOption>>(filters.ItemsSource).ToArray();

        Assert.Equal(SelectionMode.None, filters.SelectionMode);
        Assert.Equal(WorkoutResources.Current.All, options[0].Label);
    }

    [Fact]
    public void Activating_a_body_part_filter_changes_emphasis_without_moving_its_content()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var filters = Descendants(page)
            .OfType<CollectionView>()
            .Single(view => view.ItemsSource is IEnumerable<BodyPartFilterOption>);
        var option = Assert.IsAssignableFrom<IEnumerable<BodyPartFilterOption>>(filters.ItemsSource)
            .Single(item => item.Value == BodyPart.Legs);
        var chip = Assert.IsType<Border>(filters.ItemTemplate.CreateContent());
        chip.BindingContext = option;
        var normalPadding = chip.Padding;
        var normalMinimumHeight = chip.MinimumHeightRequest;
        var normalBackground = chip.BackgroundColor;

        option.SelectCommand.Execute(null);

        Assert.True(option.IsSelected);
        Assert.NotEqual(normalBackground, chip.BackgroundColor);
        Assert.Equal(normalPadding, chip.Padding);
        Assert.Equal(normalMinimumHeight, chip.MinimumHeightRequest);
        Assert.True(chip.MinimumHeightRequest >= 44);
    }

    [Fact]
    public void Body_part_filter_text_stays_vertically_centered_inside_the_touch_target()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var filters = Descendants(page)
            .OfType<CollectionView>()
            .Single(view => view.ItemsSource is IEnumerable<BodyPartFilterOption>);
        var option = Assert.IsAssignableFrom<IEnumerable<BodyPartFilterOption>>(filters.ItemsSource)
            .Single(item => item.Value == BodyPart.Legs);
        var chip = Assert.IsType<Border>(filters.ItemTemplate.CreateContent());
        chip.BindingContext = option;
        var label = Assert.IsType<Label>(chip.Content);

        Assert.Equal(LayoutOptions.Center, label.VerticalOptions);
        Assert.Equal(TextAlignment.Center, label.VerticalTextAlignment);

        option.SelectCommand.Execute(null);

        Assert.True(option.IsSelected);
        Assert.Equal(LayoutOptions.Center, label.VerticalOptions);
        Assert.Equal(TextAlignment.Center, label.VerticalTextAlignment);
    }

    [Fact]
    public void Returning_to_all_restores_the_previous_filter_chip_background()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var filters = Descendants(page)
            .OfType<CollectionView>()
            .Single(view => view.ItemsSource is IEnumerable<BodyPartFilterOption>);
        var options = Assert.IsAssignableFrom<IEnumerable<BodyPartFilterOption>>(filters.ItemsSource).ToArray();
        var all = options.Single(option => option.Value is null);
        var legs = options.Single(option => option.Value == BodyPart.Legs);
        var legsChip = Assert.IsType<Border>(filters.ItemTemplate.CreateContent());
        legsChip.BindingContext = legs;
        var normalBackground = legsChip.BackgroundColor;

        legs.SelectCommand.Execute(null);

        Assert.NotEqual(normalBackground, legsChip.BackgroundColor);

        all.SelectCommand.Execute(null);

        Assert.False(legs.IsSelected);
        Assert.Equal(normalBackground, legsChip.BackgroundColor);
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
    public void Custom_exercise_offers_one_local_image_action_without_a_library()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        var imageActions = Descendants(page)
            .OfType<Button>()
            .Where(button => button.Text is not null &&
                (button.Text == viewModel.Text.AddExerciseImage ||
                 button.Text == viewModel.Text.ChooseExerciseImage))
            .ToArray();

        var imageAction = Assert.Single(imageActions);
        Assert.Equal(viewModel.Text.AddExerciseImage, imageAction.Text);
        Assert.Empty(Descendants(page).OfType<CollectionView>());
    }

    [Fact]
    public void Custom_exercise_add_image_action_opens_the_local_picker_immediately()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var viewModel = Assert.IsType<CustomExerciseViewModel>(page.BindingContext);
        var imageAction = Descendants(page)
            .OfType<Button>()
            .Single(button => button.Text == viewModel.Text.AddExerciseImage);

        imageAction.SendClicked();

        Assert.Equal(1, _imagePicker.PickCount);
        Assert.Equal(viewModel.Text.ChooseExerciseImage, _imagePicker.LastTitle);
    }

    [Fact]
    public void Custom_exercise_fast_form_uses_equal_fields_and_a_compact_image_preview()
    {
        var page = _app.Services.GetRequiredService<CustomExercisePage>();
        var content = Assert.IsType<VerticalStackLayout>(page.FindByName("CustomExerciseForm"));
        var nameField = Assert.IsType<Border>(page.FindByName("CustomExerciseNameField"));
        var bodyPartField = Assert.IsType<Border>(page.FindByName("CustomExerciseBodyPartField"));
        var trackingField = Assert.IsType<Border>(page.FindByName("CustomExerciseTrackingField"));
        var preview = Assert.IsType<Image>(page.FindByName("CustomExerciseImagePreview"));

        Assert.Equal(12, content.Spacing);
        Assert.Equal(nameField.MinimumHeightRequest, bodyPartField.MinimumHeightRequest);
        Assert.Equal(nameField.MinimumHeightRequest, trackingField.MinimumHeightRequest);
        Assert.InRange(preview.HeightRequest, 44, 72);
        Assert.Equal(preview.HeightRequest, preview.WidthRequest);
    }

    [Fact]
    public void Create_custom_passes_the_trimmed_search_name_to_navigation()
    {
        var page = _app.Services.GetRequiredService<ExercisePickerPage>();
        var viewModel = Assert.IsType<ExercisePickerViewModel>(page.BindingContext);
        viewModel.SearchText = "  Smith Machine Row  ";
        var createButton = Descendants(page)
            .OfType<Button>()
            .Single(button => button.Text == viewModel.Text.CreateCustom);

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
    }
}
