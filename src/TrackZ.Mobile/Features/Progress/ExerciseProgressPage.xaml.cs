using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using static TrackZ.Mobile.Features.Coach.CoachCopy;
using static TrackZ.Mobile.Features.Progress.ProgressPresentation;

namespace TrackZ.Mobile.Features.Progress;

public partial class ExerciseProgressPage : ContentPage, IQueryAttributable
{
    private readonly Func<CancellationToken, Task<CoachReport>> _load;
    private readonly Func<CoachSession, bool, CancellationToken, Task> _save;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IWeightUnitPreference _units;
    private readonly IClock _clock;
    private CancellationTokenSource? _lifetime;
    private AccountSessionGeneration _generation;
    private CoachReport? _source;
    private MuscleProgressReport? _report;
    private DateOnly _start;
    private DateOnly _end;
    private int _weeks = 4;
    private Guid? _requested;
    private BodyPart? _body;
    private Guid? _exercise;
    private bool _history;
    private bool _dateEditor;
    private bool _subscribed;
#if IOS
    private Foundation.NSObject? _textSizeObserver;
#endif

    public ExerciseProgressPage(TrainingCoachSource source, CoachJournal journal,
        IAccountSessionBoundary boundary, IWeightUnitPreference units, IClock clock)
        : this(source.LoadAsync, (s, controlled, ct) => journal.SaveControlAsync(s, controlled, clock.UtcNow, ct), boundary, units, clock) { }

    internal ExerciseProgressPage(Func<CancellationToken, Task<CoachReport>> load,
        Func<CoachSession, bool, CancellationToken, Task> save,
        IAccountSessionBoundary boundary, IWeightUnitPreference units, IClock clock)
    {
        InitializeComponent();
        _load = load; _save = save; _boundary = boundary; _units = units; _clock = clock;
        _generation = boundary.Capture();
        _end = Today; _start = _end.AddDays(-27);
        Title = T("ผลการฝึก", "Progress");
        OverviewTitle.Text = Title;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime().DateTime);
    internal MuscleProgressReport? Report => _report;
    internal Guid? SelectedExercise => _exercise;
    internal BodyPart? SelectedBody => _body;

    public void ApplyQueryAttributes(IDictionary<string, object> query) => _requested =
        query.TryGetValue("exerciseId", out var value) && Guid.TryParse(Convert.ToString(value), out var id) ? id : null;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_subscribed) { _boundary.SessionReset += OnReset; _units.Changed += OnUnitsChanged; _subscribed = true; }
#if IOS
        _textSizeObserver ??= Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            new Foundation.NSString("UIContentSizeCategoryDidChangeNotification"), _ => MainThread.BeginInvokeOnMainThread(Render));
#endif
        await HandleAppearingAsync();
    }

    internal async Task HandleAppearingAsync()
    {
        _lifetime?.Cancel(); _lifetime?.Dispose();
        var lifetime = _lifetime = new CancellationTokenSource();
        if (_boundary.IsCancellationRequested(_generation)) ClearPrivateState();
        _generation = _boundary.Capture();
        if (_weeks > 0) { _end = Today; _start = _end.AddDays(1 - _weeks * 7); }
        using var lease = _boundary.CreateCancellationLease(_generation, lifetime.Token);
        ProgressContent.Clear();
        ProgressContent.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#C8FF3D"), Margin = 24 });
        try
        {
            var source = await _load(lease.Token);
            lease.Token.ThrowIfCancellationRequested();
            _source = source;
            if (_requested is { } id)
            {
                _requested = null;
                var exercise = source.Exercises.FirstOrDefault(e => e.Id == id);
                var latest = exercise?.Sessions.Where(s => s.IsCompleted).OrderByDescending(s => s.At).FirstOrDefault();
                if (exercise is not null && latest is not null)
                {
                    var date = DateOnly.FromDateTime(latest.At.ToLocalTime().DateTime);
                    if (date < _start) { _start = date.AddDays(-27); _end = date; _weeks = 0; }
                    _body = exercise.BodyPart; _exercise = id; _history = false;
                }
            }
            Rebuild();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!lease.Token.IsCancellationRequested)
            {
                ProgressContent.Clear();
                ProgressContent.Add(Label(T("ยังโหลดผลการฝึกไม่ได้", "Could not load your progress"), 18));
                ProgressContent.Add(Button(T("ลองอีกครั้ง", "Try again"), HandleAppearingAsync));
            }
        }
    }

    private void Rebuild()
    {
        if (_source is null || _boundary.IsCancellationRequested(_generation)) return;
        _report = MuscleProgressReport.Build(_source, _start, _end, TimeZoneInfo.Local);
        Render();
    }
    internal void SelectPeriod(int weeks)
    {
        if (weeks is not (4 or 12)) throw new ArgumentOutOfRangeException(nameof(weeks));
        _weeks = weeks; _end = Today; _start = _end.AddDays(1 - weeks * 7); _dateEditor = false; Rebuild();
    }
    internal Task OpenBodyAsync(BodyPart body) { _body = body; _exercise = null; _history = false; return ChangedPageAsync(); }
    internal Task OpenExerciseAsync(Guid id) { _exercise = id; _history = false; return ChangedPageAsync(); }
    internal Task BackAsync()
    {
        if (_history) _history = false;
        else if (_exercise is not null) _exercise = null;
        else _body = null;
        return ChangedPageAsync();
    }
    private async Task ChangedPageAsync()
    {
        Render();
        if (Handler is not null) await ExerciseProgressScroll.ScrollToAsync(0, 0, false);
    }
    protected override bool OnBackButtonPressed()
    {
        if (_body is null) return base.OnBackButtonPressed();
        _ = BackAsync(); return true;
    }

    private void Render()
    {
        if (_report is null) return;
        ProgressContent.Clear();
        OverviewTitle.IsVisible = _body is null;
        Shell.SetTabBarIsVisible(this, _exercise is null);
        if (_body is null)
        {
            ProgressContent.Add(Label(T("ดูว่าการฝึกแต่ละส่วนเป็นอย่างไร", "See how each muscle group is doing"), 13, true));
            ProgressContent.Add(PeriodSelector());
            if (_dateEditor) ProgressContent.Add(DateEditor());
            var range = Label(Range(_report), 12, true); range.HorizontalTextAlignment = TextAlignment.Center;
            ProgressContent.Add(range);
            ProgressContent.Add(MuscleProgressViews.Overview(_report, OpenBodyAsync));
            return;
        }
        var header = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(44)], MinimumHeightRequest = 48 };
        var back = new ImageButton { Source = "auth_back.png", WidthRequest = 44, HeightRequest = 44, CornerRadius = 22,
            BackgroundColor = Color.FromArgb("#20252A"), Padding = 12 };
        SemanticProperties.SetDescription(back, T("กลับ", "Back"));
        back.Clicked += async (_, _) => await BackAsync(); header.Add(back);
        var title = Label(_exercise is null ? BodyName(_body.Value) : _history ? T("บันทึกการฝึก", "Training log") : T("ผลการฝึกรายท่า", "Exercise progress"), 18, heading: true);
        title.HorizontalTextAlignment = TextAlignment.Center; header.Add(title, 1); ProgressContent.Add(header);
        if (_exercise is null)
        {
            var range = Label(Range(_report), 12, true); range.HorizontalTextAlignment = TextAlignment.Center;
            ProgressContent.Add(range);
            ProgressContent.Add(MuscleProgressViews.Area(_report.Areas.Single(a => a.BodyPart == _body), _units.Current, OpenExerciseAsync));
            return;
        }
        var item = _report.Areas.SelectMany(a => a.Exercises).FirstOrDefault(e => e.Exercise.Id == _exercise);
        if (item is null) { ProgressContent.Add(Label(T("ยังไม่มีบันทึกในช่วงนี้", "No sessions in this period"))); return; }
        if (_history)
        {
            ProgressContent.Add(Label(item.Exercise.Name, 20, heading: true));
            ProgressContent.Add(Label(T("จำนวนครั้งใช้เซ็ตที่ทำได้น้อยที่สุดในแต่ละครั้งฝึก", "Reps show the minimum across working sets in each session"), 12, true));
            foreach (var s in item.Sessions.Reverse())
                ProgressContent.Add(Card(Stack(4, Label(Date(s), 14, true), Label(Performance(s, _units.Current), 18),
                    Label(T($"{s.WorkingSets} เซ็ตฝึก", $"{s.WorkingSets} working sets"), 13, true))));
        }
        else ProgressContent.Add(MuscleProgressViews.Detail(item, _units.Current, controlled => SaveCheckInAsync(item, controlled),
            () => { _history = true; return ChangedPageAsync(); }));
    }

    private View PeriodSelector()
    {
        if (LargeText)
            return Columns(Button(T("4 สัปดาห์", "4 weeks"), () => { SelectPeriod(4); return Task.CompletedTask; }, _weeks == 4),
                Button(T("12 สัปดาห์", "12 weeks"), () => { SelectPeriod(12); return Task.CompletedTask; }, _weeks == 12),
                Button(T("เลือกช่วง", "Dates"), () => { _dateEditor = !_dateEditor; Render(); return Task.CompletedTask; }, _weeks == 0));
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)], MinimumHeightRequest = 44 };
        var track = Card(new Grid(), new Thickness(0));
        track.HeightRequest = 32; track.VerticalOptions = LayoutOptions.Center;
        track.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 7 };
        grid.Add(track); Grid.SetColumnSpan(track, 3);
        var values = new[] { 4, 12, 0 };
        var names = new[] { T("4 สัปดาห์", "4 weeks"), T("12 สัปดาห์", "12 weeks"), T("เลือกช่วง", "Dates") };
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i]; var selected = _weeks == value;
            if (selected)
                grid.Add(new Microsoft.Maui.Controls.Border { BackgroundColor = Color.FromArgb("#C8FF3D"), StrokeThickness = 0,
                    HeightRequest = 30, Margin = 1, VerticalOptions = LayoutOptions.Center,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, InputTransparent = true }, i);
            var button = Button(names[i], () =>
            {
                if (value == 0) { _dateEditor = !_dateEditor; Render(); }
                else SelectPeriod(value);
                return Task.CompletedTask;
            }, selected);
            button.BackgroundColor = Colors.Transparent; button.BorderWidth = 0; button.FontSize = 12;
            button.TextColor = Color.FromArgb(selected ? "#111609" : "#A7AFB8");
            grid.Add(button, i);
        }
        return grid;
    }

    private View DateEditor()
    {
        var from = new DatePicker { Date = _start.ToDateTime(TimeOnly.MinValue), MaximumDate = Today.ToDateTime(TimeOnly.MinValue),
            TextColor = Colors.White, FontFamily = "NotoSansThaiRegular", FontSize = 14, Format = "d MMM yyyy", MinimumHeightRequest = 44 };
        var to = new DatePicker { Date = _end.ToDateTime(TimeOnly.MinValue), MaximumDate = Today.ToDateTime(TimeOnly.MinValue),
            TextColor = Colors.White, FontFamily = "NotoSansThaiRegular", FontSize = 14, Format = "d MMM yyyy", MinimumHeightRequest = 44 };
        SemanticProperties.SetDescription(from, T("วันที่เริ่มต้น", "Start date")); SemanticProperties.SetDescription(to, T("วันที่สิ้นสุด", "End date"));
        var error = Label("", 12, true);
        return Card(Stack(8, Columns(Stack(2, Label(T("ตั้งแต่", "From"), 12, true), from), Stack(2, Label(T("ถึง", "To"), 12, true), to)), error,
            Button(T("แสดงผล", "Apply"), () =>
            {
                if (from.Date is not { } first || to.Date is not { } last || first > last || (last - first).TotalDays > 365)
                { error.Text = T("เลือกวันเริ่มก่อนวันสิ้นสุด ในช่วงไม่เกิน 1 ปี", "Choose a start before the end, within one year"); return Task.CompletedTask; }
                _start = DateOnly.FromDateTime(first); _end = DateOnly.FromDateTime(last); _weeks = 0; _dateEditor = false; Rebuild(); return Task.CompletedTask;
            }, true)));
    }

    internal async Task SaveCheckInAsync(ExerciseProgressEvidence item, bool controlled)
    {
        if (_lifetime is null || item.Latest is null) return;
        var generation = _generation;
        try
        {
            using var lease = _boundary.CreateCancellationLease(generation, _lifetime.Token);
            var fresh = await _load(lease.Token);
            var session = fresh.Exercises.FirstOrDefault(e => e.Id == item.Exercise.Id)?.Sessions
                .FirstOrDefault(s => s.WorkoutId == item.Latest.WorkoutId);
            if (session is null || session.LastSetId != item.Latest.LastSetId || session.LastEditedAt != item.Latest.LastEditedAt)
            { lease.Token.ThrowIfCancellationRequested(); _source = fresh; Rebuild(); return; }
            if (await _boundary.TryCommitAsync(generation, ct => _save(session, controlled, ct), lease.Token))
                await HandleAppearingAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_boundary.IsCancellationRequested(generation))
                await DisplayAlertAsync(T("ยังบันทึกไม่ได้", "Could not save"), T("ลองอีกครั้ง ข้อมูลการฝึกยังอยู่ครบ", "Try again. Your training is safe."), T("ตกลง", "OK"));
        }
    }

    private void OnUnitsChanged(object? sender, EventArgs args) => Render();
    private void OnReset(object? sender, EventArgs args)
    {
        _lifetime?.Cancel();
        if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(ClearPrivateState); else ClearPrivateState();
    }
    private void ClearPrivateState()
    {
        _source = null; _report = null; _requested = null; _body = null; _exercise = null; _history = false;
        _dateEditor = false; _weeks = 4; ProgressContent.Clear();
    }
    protected override void OnDisappearing()
    {
        _lifetime?.Cancel();
#if IOS
        _textSizeObserver?.Dispose(); _textSizeObserver = null;
#endif
        if (_subscribed) { _boundary.SessionReset -= OnReset; _units.Changed -= OnUnitsChanged; _subscribed = false; }
        base.OnDisappearing();
    }
}
