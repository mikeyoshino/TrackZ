using System.Globalization;
using System.Reflection;
using Microsoft.Maui.Dispatching;
using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.Coach;

public sealed class CoachUiBehaviorTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly IDispatcherProvider _originalDispatcher = DispatcherProvider.Current;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"trackz-coach-ui-{Guid.NewGuid():N}");

    public CoachUiBehaviorTests()
    {
        Directory.CreateDirectory(_root);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
    }

    [Theory]
    [InlineData(false, "en-US", "Today")]
    [InlineData(true, "en-US", "Today")]
    [InlineData(false, "th-TH", "วันนี้")]
    [InlineData(true, "th-TH", "วันนี้")]
    public void Week_keeps_today_ring_separate_from_training_status(bool trained, string culture, string todayText)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var monday = new DateOnly(2026, 8, 31);
        var days = Enumerable.Range(0, 7)
            .Select(i => new CoachDay(monday.AddDays(i), i == 5 && trained, i == 5)).ToArray();
        var view = CoachViews.Week(new CoachReport(monday, days, [], [], 0), 3);
        var rings = Descendants(view).OfType<Border>().Where(b => b.Content is Border).ToArray();
        Assert.Equal(7, rings.Length);
        for (var i = 0; i < rings.Length; i++)
        {
            var ring = rings[i];
            var status = Assert.IsType<Border>(ring.Content);
            Assert.True(ring.WidthRequest > status.WidthRequest);
            Assert.True(ring.Padding.Left > 0);
            Assert.Equal(i == 5 ? 1.5 : 0, ring.StrokeThickness);
            Assert.Equal(i == 5 && trained ? "✓" : i < 5 ? "–" : "", Assert.IsType<Label>(status.Content).Text);
            Assert.Equal(i == 5 && trained
                ? Color.FromArgb("#C8FF3D")
                : i < 5 ? Color.FromArgb("#252B31") : Colors.Transparent, status.BackgroundColor);
            if (i == 5)
                Assert.Contains(todayText, SemanticProperties.GetDescription(ring));
        }
        Assert.Single(Descendants(view).OfType<Label>(), l => l.Text == todayText);
    }

    [Fact]
    public void Week_distinguishes_missed_past_days_from_future_days()
    {
        var monday = new DateOnly(2026, 8, 31);
        var days = Enumerable.Range(0, 7)
            .Select(i => new CoachDay(monday.AddDays(i), i == 2, i == 5)).ToArray();
        var view = CoachViews.Week(new CoachReport(monday, days, [], [], 0), 3);
        var statuses = Descendants(view).OfType<Border>()
            .Where(border => border.Content is Border)
            .Select(ring => Assert.IsType<Border>(ring.Content)).ToArray();

        Assert.Equal(Color.FromArgb("#252B31"), statuses[0].BackgroundColor);
        Assert.Equal("–", Assert.IsType<Label>(statuses[0].Content).Text);
        Assert.Equal(Color.FromArgb("#C8FF3D"), statuses[2].BackgroundColor);
        Assert.Equal("✓", Assert.IsType<Label>(statuses[2].Content).Text);
        Assert.Equal(Colors.Transparent, statuses[6].BackgroundColor);
        Assert.Equal("", Assert.IsType<Label>(statuses[6].Content).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pain_advice_wins_over_recovery_in_another_body_area(bool fullReport)
    {
        var painExercise = new CoachExercise(
            Guid.NewGuid(), "Painful press", BodyPart.Chest, TrackingMode.Weighted,
            [], new CoachRecommendation(CoachAction.Pain), false);
        var report = new CoachReport(
            DateOnly.FromDateTime(Now.UtcDateTime),
            Enumerable.Range(0, 7).Select(index => new CoachDay(
                DateOnly.FromDateTime(Now.UtcDateTime).AddDays(index), false, index == 0)).ToArray(),
            [new CoachArea(BodyPart.Legs, 8, 0, false,
                new CoachRecovery(BodyPart.Legs, DateOnly.FromDateTime(Now.UtcDateTime), Now, false))],
            [painExercise],
            0);

        var view = fullReport
            ? CoachViews.Report(report, null, new KilogramPreference(), _ => Task.CompletedTask, (_, _) => Task.CompletedTask)
            : CoachViews.HomeAdvice(report, new KilogramPreference(), _ => Task.CompletedTask, (_, _) => Task.CompletedTask);

        Assert.NotNull(view);
        Assert.Contains(
            Descendants(view!).OfType<Label>(),
            label => label.Text == "Painful press");
        Assert.DoesNotContain(
            Descendants(view!).OfType<Label>(),
            label => label.Text?.Contains("Take it easier", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Missing_initial_context_keeps_navigation_and_retry_loads_new_active_exercise()
    {
        var fixture = CreateFixture();
        var page = fixture.CreatePage();

        Assert.Contains(Buttons(page), button => button.Text == "‹");
        Assert.Contains(Buttons(page), button => button.Text == "Skip and return to workout");
        Assert.Contains(Descendants(page).OfType<ActivityIndicator>(), indicator => indicator.IsRunning);

        InvokeAppearing(page);
        await WaitUntilAsync(() => Buttons(page).Any(button => button.Text == "Try again"));

        Assert.Contains(Buttons(page), button => button.Text == "‹");
        Assert.Contains(Buttons(page), button => button.Text == "Skip and return to workout");

        await fixture.SeedActiveExerciseAsync();
        Buttons(page).Single(button => button.Text == "Try again").SendClicked();
        await WaitUntilAsync(() => Labels(page).Any(label => label.Text == "Today's sets"));

        Assert.Contains(Labels(page), label => label.Text == "Bench press");
        Assert.Empty((await fixture.Journal.ReadAsync()).Assessments);
    }

    [Fact]
    public async Task Initial_load_error_back_and_skip_leave_without_writing_journal_data()
    {
        var fixture = CreateFixture();

        foreach (var action in new[] { "‹", "Skip and return to workout" })
        {
            var page = fixture.CreatePage();
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(page, animated: false);
            InvokeAppearing(page);
            await WaitUntilAsync(() => Buttons(page).Any(button => button.Text == "Try again"));

            Buttons(page).Single(button => button.Text == action).SendClicked();
            await WaitUntilAsync(() => navigation.Navigation.NavigationStack.Count == 1);
        }

        Assert.Empty((await fixture.Journal.ReadAsync()).Assessments);
        Assert.Empty((await fixture.Journal.ReadAsync()).Warmups);
    }

    [Fact]
    public async Task Journal_write_disables_sibling_choices_and_reenables_them_after_failure()
    {
        var boundary = new GatedFailingBoundary();
        var fixture = CreateFixture(boundary);
        await fixture.SeedActiveExerciseAsync();
        var page = fixture.CreatePage();
        InvokeAppearing(page);
        await WaitUntilAsync(() => Labels(page).Any(label => label.Text == "Today's sets"));

        Buttons(page).Single(button => button.Text?.Contains("A few reps left", StringComparison.Ordinal) == true).SendClicked();
        await WaitUntilAsync(() => Buttons(page).Any(button => button.Text?.StartsWith("●", StringComparison.Ordinal) == true));
        var siblingChoice = Buttons(page).Single(button => button.Text?.Contains("Several reps left", StringComparison.Ordinal) == true);
        var save = Buttons(page).Single(button => button.Text == "Save and continue");

        save.SendClicked();
        await boundary.CommitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(siblingChoice.IsEnabled);

        boundary.FailCommit();
        await WaitUntilAsync(() => Labels(page).Any(label => label.Text?.Contains("Could not save", StringComparison.Ordinal) == true));

        Assert.True(siblingChoice.IsEnabled);
        Assert.True(Buttons(page).Single(button => button.Text == "Save and continue").IsEnabled);
        Assert.Empty((await fixture.Journal.ReadAsync()).Assessments);
    }

    [Fact]
    public async Task Set_added_after_answering_requires_a_fresh_answer_before_saving()
    {
        var fixture = CreateFixture();
        await fixture.SeedActiveExerciseAsync();
        var page = fixture.CreatePage();
        InvokeAppearing(page);
        await WaitUntilAsync(() => Labels(page).Any(label => label.Text == "Today's sets"));

        Buttons(page).Single(button => button.Text?.Contains("A few reps left", StringComparison.Ordinal) == true).SendClicked();
        await WaitUntilAsync(() => Buttons(page).Any(button => button.Text?.StartsWith("●", StringComparison.Ordinal) == true));
        await fixture.AddSetAsync();

        Buttons(page).Single(button => button.Text == "Save and continue").SendClicked();
        await WaitUntilAsync(() => Labels(page).Any(label => label.Text?.Contains("sets changed", StringComparison.OrdinalIgnoreCase) == true));

        Assert.Equal(2, Labels(page).Count(label => label.Text?.StartsWith("#", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(Buttons(page), button => button.Text?.StartsWith("●", StringComparison.Ordinal) == true);
        Assert.False(Buttons(page).Single(button => button.Text == "Save and continue").IsEnabled);
        Assert.Empty((await fixture.Journal.ReadAsync()).Assessments);
    }

    public ValueTask DisposeAsync()
    {
        DispatcherProvider.SetCurrent(_originalDispatcher);
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }

    private Fixture CreateFixture(IAccountSessionBoundary? boundary = null) =>
        new(_root, boundary ?? new AccountSessionBoundary());

    private static void InvokeAppearing(ExerciseCheckInPage page)
    {
        var method = typeof(ExerciseCheckInPage)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Single(candidate => candidate.Name == "OnAppearing" && candidate.GetParameters().Length == 0);
        method.Invoke(page, null);
    }

    private static Button[] Buttons(IVisualTreeElement root) => Descendants(root).OfType<Button>().ToArray();
    private static Label[] Labels(IVisualTreeElement root) => Descendants(root).OfType<Label>().ToArray();

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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class Fixture
    {
        private readonly string _root;
        private readonly IAccountSessionBoundary _boundary;
        private readonly TrackZLocalDatabase _database;
        private readonly LocalWorkoutRepository _repository;
        private readonly ExerciseCache _cache;
        private readonly ExerciseGuidancePreferenceStore _increments;
        private readonly IWeightUnitPreference _units;

        public Fixture(string root, IAccountSessionBoundary boundary)
        {
            _root = root;
            _boundary = boundary;
            _database = new TrackZLocalDatabase(Path.Combine(root, "workouts.db"));
            _repository = new LocalWorkoutRepository(_database);
            _cache = new ExerciseCache(Path.Combine(root, "exercises.db"));
            Journal = new CoachJournal(_database);
            var preferences = new MemoryPreferences();
            _increments = new ExerciseGuidancePreferenceStore(preferences);
            _units = new WeightUnitPreference(preferences);
        }

        public Guid ExerciseId { get; } = Guid.NewGuid();
        public CoachJournal Journal { get; }

        public ExerciseCheckInPage CreatePage()
        {
            var source = new TrainingCoachSource(
                _repository, _cache, Journal, _increments, new FixedClock(Now), TimeZoneInfo.Utc);
            return new ExerciseCheckInPage(
                source, Journal, _boundary, _units, _increments, new FixedClock(Now), ExerciseId);
        }

        public async Task SeedActiveExerciseAsync()
        {
            await _cache.ReplaceAllAsync([
                new ExerciseSummaryDto(
                    ExerciseId, "Bench press", BodyPart.Chest, TrackingMode.Weighted,
                    null, null, null, null, false)
            ], Now);
            var coordinator = new ActiveWorkoutCoordinator(
                _repository, new AccountSessionBoundary(), new FixedClock(Now));
            await coordinator.StartAsync([new WorkoutExerciseSelection(ExerciseId, TrackingMode.Weighted)]);
            await coordinator.SaveSetAsync(ExerciseId, new LocalSet(60m, null, 8));
        }

        public Task AddSetAsync() => new ActiveWorkoutCoordinator(
            _repository, new AccountSessionBoundary(), new FixedClock(Now.AddMinutes(1)))
            .SaveSetAsync(ExerciseId, new LocalSet(60m, null, 9));
    }

    private sealed class GatedFailingBoundary : IAccountSessionBoundary
    {
        private readonly AccountSessionBoundary _inner = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CommitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? SessionReset
        {
            add => _inner.SessionReset += value;
            remove => _inner.SessionReset -= value;
        }

        public AccountSessionGeneration Capture() => _inner.Capture();
        public bool IsCancellationRequested(AccountSessionGeneration generation) => _inner.IsCancellationRequested(generation);
        public AccountSessionCancellationLease CreateCancellationLease(AccountSessionGeneration generation, CancellationToken cancellationToken = default) =>
            _inner.CreateCancellationLease(generation, cancellationToken);
        public bool TryStartSessionPhase(AccountSessionGeneration generation, Action phase, CancellationToken cancellationToken = default) =>
            _inner.TryStartSessionPhase(generation, phase, cancellationToken);
        public Task ResetAsync(Func<CancellationToken, Task> reset, CancellationToken cancellationToken = default) =>
            _inner.ResetAsync(reset, cancellationToken);
        public Task<bool> TryResetAsync(AccountSessionGeneration generation, Func<CancellationToken, Task> reset, CancellationToken cancellationToken = default) =>
            _inner.TryResetAsync(generation, reset, cancellationToken);
        public Task<bool> TryResetIfAsync(AccountSessionGeneration generation, Func<CancellationToken, Task<bool>> authorizeReset, Func<CancellationToken, Task> reset, CancellationToken cancellationToken = default) =>
            _inner.TryResetIfAsync(generation, authorizeReset, reset, cancellationToken);

        public async Task<bool> TryCommitAsync(
            AccountSessionGeneration generation,
            Func<CancellationToken, Task> mutation,
            CancellationToken cancellationToken = default)
        {
            CommitStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            throw new IOException("Journal write failed.");
        }

        public void FailCommit() => _release.TrySetResult();
    }

    private sealed class MemoryPreferences : IWorkoutPreferenceStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
    }

    private sealed class KilogramPreference : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit unit) { }
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
