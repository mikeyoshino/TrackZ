using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Progress;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class ProgressExerciseFocusTests : IDisposable
{
    private readonly IDispatcherProvider _original = DispatcherProvider.Current;
    private static readonly Guid Id = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    public ProgressExerciseFocusTests() => DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
    public void Dispose() => DispatcherProvider.SetCurrent(_original);
    private static CoachReport Data(int days = 1)
    {
        var s = new CoachSession(Guid.NewGuid(), Id, Now.AddDays(-days), TrackingMode.Weighted, 40, null,
            10, 3, 0, null, false, false) { LastSetId = Guid.NewGuid() };
        return new(new(2026, 8, 31), [], [], [new(Id, "Bench", BodyPart.Chest, TrackingMode.Weighted, [s], new(CoachAction.Hold), false)], 0);
    }
    private static ExerciseProgressPage Page(Func<CancellationToken, Task<CoachReport>>? load = null,
        IAccountSessionBoundary? boundary = null, Func<CoachSession, bool, CancellationToken, Task>? save = null) =>
        new(load ?? (_ => Task.FromResult(Data())), save ?? ((_, _, _) => Task.CompletedTask),
            boundary ?? new AccountSessionBoundary(), new Kilograms(), new Clock());

    [Fact]
    public async Task Deep_link_opens_requested_exercise_once_and_back_keeps_its_body_group()
    {
        var page = Page();
        page.ApplyQueryAttributes(new Dictionary<string, object> { ["exerciseId"] = Id.ToString() });
        await page.HandleAppearingAsync();
        Assert.Equal(Id, page.SelectedExercise);
        Assert.Equal(BodyPart.Chest, page.SelectedBody);
        await page.BackAsync();
        await page.HandleAppearingAsync();
        Assert.Null(page.SelectedExercise);
        Assert.Equal(BodyPart.Chest, page.SelectedBody);
    }

    [Fact]
    public async Task Range_survives_drilldown_and_return()
    {
        var page = Page();
        await page.HandleAppearingAsync();
        page.SelectPeriod(12);
        await page.OpenBodyAsync(BodyPart.Chest);
        await page.OpenExerciseAsync(Id);
        await page.BackAsync();
        await page.BackAsync();
        Assert.Null(page.SelectedBody);
        Assert.Equal(83, page.Report!.End.DayNumber - page.Report.Start.DayNumber);
    }

    [Fact]
    public async Task Deep_link_to_older_history_adjusts_range_instead_of_showing_empty_detail()
    {
        var page = Page(_ => Task.FromResult(Data(90)));
        page.ApplyQueryAttributes(new Dictionary<string, object> { ["exerciseId"] = Id.ToString() });
        await page.HandleAppearingAsync();
        Assert.Equal(Id, page.SelectedExercise);
        Assert.Single(page.Report!.Areas[0].Exercises);
    }

    [Fact]
    public async Task Late_load_cannot_restore_previous_accounts_private_data()
    {
        var boundary = new AccountSessionBoundary();
        var pending = new TaskCompletionSource<CoachReport>();
        var page = Page(_ => pending.Task, boundary);
        var appearance = page.HandleAppearingAsync();
        await boundary.ResetAsync(_ => Task.CompletedTask);
        pending.SetResult(Data());
        await appearance;
        Assert.Null(page.Report);
    }

    [Fact]
    public async Task Edited_session_does_not_save_a_checkin_against_old_set_data()
    {
        var first = Data(); var calls = 0; var saves = 0;
        var second = first with { Exercises = [first.Exercises[0] with
            { Sessions = [first.Exercises[0].Sessions[0] with { LastEditedAt = Now }] }] };
        var page = Page(_ => Task.FromResult(++calls == 1 ? first : second),
            save: (_, _, _) => { saves++; return Task.CompletedTask; });
        await page.HandleAppearingAsync();
        await page.SaveCheckInAsync(page.Report!.Areas[0].Exercises[0], true);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task Failed_load_can_retry_without_losing_the_selected_period()
    {
        var calls = 0;
        var page = Page(_ => ++calls == 1 ? Task.FromException<CoachReport>(new IOException()) : Task.FromResult(Data()));
        page.SelectPeriod(12);
        await page.HandleAppearingAsync();
        Assert.Null(page.Report);
        await page.HandleAppearingAsync();
        Assert.Equal(83, page.Report!.End.DayNumber - page.Report.Start.DayNumber);
    }

    [Fact]
    public async Task Empty_history_keeps_all_six_body_groups_without_fabricated_progress()
    {
        var empty = Data() with { Exercises = [] };
        var page = Page(_ => Task.FromResult(empty));
        await page.HandleAppearingAsync();
        Assert.Equal(6, page.Report!.Areas.Count);
        Assert.Equal(0, page.Report.ImprovedAreas);
        Assert.All(page.Report.Areas, area => { Assert.Empty(area.Exercises); Assert.Equal(0, area.Comparable); });
        var title = page.FindByName<Label>("OverviewTitle");
        Assert.True(title.IsVisible);
        Assert.Equal(SemanticHeadingLevel.Level1, SemanticProperties.GetHeadingLevel(title));
        await page.OpenBodyAsync(BodyPart.Legs);
        Assert.False(title.IsVisible);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    private sealed class Kilograms : IWeightUnitPreference
    {
        public WeightDisplayUnit Current => WeightDisplayUnit.Kilograms;
        public event EventHandler? Changed { add { } remove { } }
        public void Set(WeightDisplayUnit value) { }
    }
    private sealed class InlineDispatcherProvider : IDispatcherProvider { public IDispatcher GetForCurrentThread() => new InlineDispatcher(); }
    private sealed class InlineDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
    }
}
