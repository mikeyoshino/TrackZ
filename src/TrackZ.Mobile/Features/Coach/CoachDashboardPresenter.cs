using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using static TrackZ.Mobile.Features.Coach.CoachCopy;

namespace TrackZ.Mobile.Features.Coach;

/// <summary>Owns the lifetime of one visible dashboard and fences all private UI/write work.</summary>
internal sealed class CoachDashboardPresenter(
    TrainingCoachSource source, CoachJournal journal, IAccountSessionBoundary boundary,
    IWeightUnitPreference units, IClock clock, ContentPage page) : IDisposable
{
    private CancellationTokenSource? _lifetime;
    private AccountSessionGeneration _generation;
    private Func<Task>? _reload;
    private Action? _clear;
    private CoachReport? _report;

    internal void Activate(Func<Task> reload, Action clear)
    {
        Dispose(); _generation = boundary.Capture(); _lifetime = new CancellationTokenSource();
        _reload = reload; _clear = clear; boundary.SessionReset += OnReset;
    }

    internal async Task<CoachReport?> LoadAsync()
    {
        if (_lifetime is null) return null;
        using var lease = boundary.CreateCancellationLease(_generation, _lifetime.Token);
        var report = await source.LoadAsync(lease.Token);
        lease.Token.ThrowIfCancellationRequested();
        _report = report; return report;
    }

    internal View Week(CoachReport report, int? goal) => CoachViews.Week(report, goal);
    internal View? Advice(CoachReport report) => CoachViews.HomeAdvice(report, units, OpenAdviceAsync, AnswerRecoveryAsync);
    internal View Report(CoachReport report, int? goal) => CoachViews.Report(report, goal, units, OpenAdviceAsync, AnswerRecoveryAsync);

    private async Task AnswerRecoveryAsync(CoachArea area, bool ready)
    {
        if (_report is null || _lifetime is null) return;
        try
        {
            using var lease = boundary.CreateCancellationLease(_generation, _lifetime.Token);
            if (await boundary.TryCommitAsync(_generation, ct => journal.SaveRecoveryAsync(new CoachRecovery(area.BodyPart, _report.Week, clock.UtcNow, ready), ct), lease.Token)
                && _reload is not null) await _reload();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { await ShowFailureAsync(); }
    }

    private async Task OpenAdviceAsync(CoachExercise exercise)
    {
        if (_lifetime is null || boundary.IsCancellationRequested(_generation)) return;
        var detail = new ContentPage { Title = T("ครั้งหน้าลองแบบนี้", "For next time"), SafeAreaEdges = SafeAreaEdges.All };
        detail.SetDynamicResource(VisualElement.BackgroundColorProperty, "TrackZBackground");
        Shell.SetTabBarIsVisible(detail, false);
        var content = CoachUi.Stack(CoachUi.Label(exercise.Name, 24, heading: true),
            CoachUi.Card(CoachUi.Stack(CoachUi.Label(CoachCopy.Title(exercise.Recommendation), 22, heading: true),
                CoachUi.Label(Target(exercise, units.Current), 24),
                CoachUi.Label(T("ทำไมแนะนำแบบนี้?", "Why this suggestion?"), 17, heading: true),
                CoachUi.Label(Reason(exercise.Recommendation), 15, true))),
            CoachUi.Label(T("ไม่จำเป็นต้องเพิ่มทุกครั้ง ถ้าท่าเริ่มเปลี่ยนให้คงเดิม", "You do not have to increase every time. Keep your current level if technique changes."), 14, true),
            CoachUi.Label(LocalNotice, 12, true));
        // Targets are accepted in the exercise check-in where current data and form can be verified.
        content.Children.Add(CoachUi.Button(T("กลับ", "Back"), () => detail.Navigation.PopAsync(false), true));
        content.Padding = 20; detail.Content = new ScrollView { Content = content };
        await page.Navigation.PushAsync(detail, false);
    }

    private Task ShowFailureAsync() => boundary.IsCancellationRequested(_generation) ? Task.CompletedTask
        : page.DisplayAlertAsync(T("ยังบันทึกไม่ได้", "Could not save"), T("ลองอีกครั้ง ข้อมูลการฝึกยังอยู่ครบ", "Please try again. Your training is safe."), T("ตกลง", "OK"));
    private void OnReset(object? sender, EventArgs args)
    {
        _lifetime?.Cancel(); _report = null;
        MainThread.BeginInvokeOnMainThread(() => _clear?.Invoke());
    }
    public void Dispose()
    {
        boundary.SessionReset -= OnReset;
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; _report = null;
    }
}
