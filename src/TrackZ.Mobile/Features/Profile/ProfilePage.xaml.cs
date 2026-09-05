using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Identity;
using static TrackZ.Mobile.Features.History.HistoryPresentation;
using static TrackZ.Mobile.Features.History.HistoryCalendarViews;

namespace TrackZ.Mobile.Features.Profile;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _viewModel;
    private readonly IWeightUnitPreference _units;
    private bool _unitsSubscribed;
    private readonly TrainingScheduleStore _schedules;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IClock _clock;
    private CancellationTokenSource? _lifetime;
    private AccountSessionGeneration _generation;
    private TrainingSchedule _plan = TrainingSchedule.Empty;
    private HashSet<DayOfWeek> _days = [];
    private readonly Dictionary<DayOfWeek, Button> _dayButtons = [];
    private Label? _dayCount;
    private bool _saving;
    private bool _loaded;
    public ProfilePage(
        ProfileViewModel viewModel,
        IWeightUnitPreference units,
        ProfileSignOutController signOut,
        WorkoutTextSet workoutText,
        TrainingScheduleStore schedules,
        IAccountSessionBoundary boundary,
        IClock clock)
    {
        _viewModel = viewModel;
        _units = units;
        SignOut = signOut;
        WorkoutText = workoutText;
        _schedules = schedules; _boundary = boundary; _clock = clock;
        InitializeComponent();
        BindingContext = _viewModel;
        ProfileSubtitle.Text = T("ปรับการฝึกให้เหมาะกับคุณ", "Make training work for you");
        WeightUnitTitle.Text = T("หน่วยน้ำหนัก", "Weight unit");
        HapticsTitle.Text = T("สั่นเมื่อกด", "Haptic feedback");
        SavePlanButton.Text = T("บันทึกแผนฝึก", "Save training plan");
        foreach (var button in new[] { ThaiLanguageButton, EnglishLanguageButton, KilogramsButton, PoundsButton, SavePlanButton })
        {
            button.FontFamily = "NotoSansThaiRegular";
            button.FontAttributes = FontAttributes.None;
            button.FontSize = 14;
        }
        CompactSetting(LanguageSettingsCard);
        CompactSetting(WeightUnitSettingsCard);
    }
    public WorkoutTextSet WorkoutText { get; }
    public ProfileSignOutController SignOut { get; }
    public bool IsKilogramsSelected => _units.Current == WeightDisplayUnit.Kilograms;
    public bool IsPoundsSelected => _units.Current == WeightDisplayUnit.Pounds;

    private static void CompactSetting(Border card)
    {
        if (card.Content is not VerticalStackLayout content || content.Children.Count < 2
            || content.Children[0] is not Label title || content.Children[1] is not Grid choices) return;
        content.Children.Remove(title); content.Children.Remove(choices);
        title.FontFamily = "NotoSansThaiRegular"; title.FontSize = 14; title.FontAttributes = FontAttributes.None;
        title.VerticalTextAlignment = TextAlignment.Center;
        card.Padding = 12;
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(2, GridUnitType.Star))], ColumnSpacing = 8 };
        row.Add(title); row.Add(choices, 1); content.Children.Insert(0, row);
        foreach (var button in choices.Children.OfType<Button>())
        {
            button.BorderWidth = 1;
            button.SetDynamicResource(Microsoft.Maui.Controls.Button.BorderColorProperty, "TrackZBorder");
            button.Padding = new Thickness(8, 4);
        }
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _lifetime?.Cancel(); _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        _generation = _boundary.Capture();
        _boundary.SessionReset += OnReset;
        SubscribeUnits();
        RefreshUnitSelection();
        HapticsSwitch.IsToggled = Preferences.Default.Get("trackz_haptics_enabled", true);
        ReduceMotionSwitch.IsToggled = Preferences.Default.Get("trackz_reduce_motion", false);
        await _viewModel.LoadAsync();
        await LoadScheduleAsync();
    }
    protected override void OnDisappearing()
    {
        UnsubscribeUnits();
        _boundary.SessionReset -= OnReset;
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        base.OnDisappearing();
    }
    private void OnKilogramsClicked(object? sender, EventArgs args) => _units.Set(WeightDisplayUnit.Kilograms);
    private void OnPoundsClicked(object? sender, EventArgs args) => _units.Set(WeightDisplayUnit.Pounds);
    private void OnHapticsToggled(object? sender, ToggledEventArgs args) => Preferences.Default.Set("trackz_haptics_enabled", args.Value);
    private void OnReduceMotionToggled(object? sender, ToggledEventArgs args) => Preferences.Default.Set("trackz_reduce_motion", args.Value);

    private void SubscribeUnits()
    {
        if (_unitsSubscribed) return;
        _units.Changed += OnUnitsChanged;
        _unitsSubscribed = true;
    }

    private void UnsubscribeUnits()
    {
        if (!_unitsSubscribed) return;
        _units.Changed -= OnUnitsChanged;
        _unitsSubscribed = false;
    }

    private void OnUnitsChanged(object? sender, EventArgs args) => RefreshUnitSelection();

    private void RefreshUnitSelection()
    {
        OnPropertyChanged(nameof(IsKilogramsSelected));
        OnPropertyChanged(nameof(IsPoundsSelected));
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime().DateTime);

    private async Task LoadScheduleAsync()
    {
        if (_lifetime is null) return;
        _loaded = false; SavePlanButton.IsEnabled = false;
        ScheduleHost.Content = new ActivityIndicator { IsRunning = true };
        try
        {
            using var lease = _boundary.CreateCancellationLease(_generation, _lifetime.Token);
            var plan = await _schedules.ReadAsync(lease.Token);
            lease.Token.ThrowIfCancellationRequested();
            _plan = plan;
            _days = plan.Revisions.LastOrDefault()?.Days.ToHashSet() ?? [];
            _loaded = true;
            RenderSchedule();
        }
        catch (OperationCanceledException) { }
        catch
        {
            ScheduleHost.Content = Stack(Label(T("ยังโหลดแผนฝึกไม่ได้", "Could not load your plan")),
                Button(T("ลองอีกครั้ง", "Try again"), () => _ = LoadScheduleAsync()));
        }
    }

    private void RenderSchedule()
    {
        _dayButtons.Clear();
        var heading = Label(T("เลือกวันฝึกของคุณ", "Choose your training days"), 17);
        _dayCount = Label("", 18);
        _dayCount.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "TrackZPrimary");
        var grid = new Grid { ColumnSpacing = 4 };
        var shortNames = Thai ? new[] { "จ.", "อ.", "พ.", "พฤ.", "ศ.", "ส.", "อา." }
            : new[] { "M", "T", "W", "T", "F", "S", "S" };
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new(GridLength.Star));
            var day = (DayOfWeek)((i + 1) % 7);
            var button = Button(shortNames[i], () =>
            {
                if (!_days.Remove(day)) _days.Add(day);
                UpdateDays();
            });
            button.Padding = 0; button.CornerRadius = 22; button.HeightRequest = 44;
            button.BorderWidth = 1;
            _dayButtons.Add(day, button); grid.Add(button, i);
        }
        var body = Stack(heading, _dayCount, grid,
            Label(T("จำนวนวันที่เลือกคือเป้าหมายต่อสัปดาห์", "Selected days set your weekly goal"), 13, true));
        var current = _plan.At(Today);
        if (current is not null)
            body.Add(Label(T("แผนปัจจุบัน: ", "Current plan: ") + DayNames(current.Days), 13, true));
        var pending = _plan.Revisions.FirstOrDefault(revision => revision.EffectiveFrom > Today);
        if (pending is not null)
            body.Add(Label(T($"เริ่ม {pending.EffectiveFrom:d MMM}: ", $"From {pending.EffectiveFrom:d MMM}: ") + DayNames(pending.Days), 13));
        body.Add(Label(T($"เป้าหมายสัปดาห์นี้ {_plan.GoalForWeek(Today, _viewModel.WeeklyGoal)} วัน · ฝึกนอกแผนก็นับได้",
            $"This week's goal: {_plan.GoalForWeek(Today, _viewModel.WeeklyGoal)} days · Off-plan training counts too"), 13, true));
        body.Add(Label(T("ตารางวันฝึกเก็บเฉพาะเครื่องนี้ ยังไม่ซิงก์ข้ามอุปกรณ์", "Training schedule is stored on this device, not synced across devices"), 12, true));
        ScheduleHost.Content = Card(body, 12);
        UpdateDays();
    }

    private static string DayNames(IEnumerable<DayOfWeek> days) => string.Join(" · ", days.OrderBy(day => ((int)day + 6) % 7)
        .Select(day => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day)));

    private void UpdateDays()
    {
        if (_dayCount is not null) _dayCount.Text = _days.Count == 0 ? T("เลือกอย่างน้อย 1 วัน", "Select at least one day")
            : T($"{_days.Count} วัน / สัปดาห์", $"{_days.Count} days / week");
        foreach (var (day, button) in _dayButtons)
        {
            var selected = _days.Contains(day);
            HistoryCalendarViews.Background(button, selected ? "TrackZPrimary" : "TrackZSurfaceRaised");
            button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, selected ? "TrackZPrimaryContrast" : "TrackZTextPrimary");
            button.SetDynamicResource(Microsoft.Maui.Controls.Button.BorderColorProperty, selected ? "TrackZPrimary" : "TrackZBorder");
            SemanticProperties.SetDescription(button, System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day)
                + (selected ? T(" เลือกอยู่", " selected") : T(" ไม่ได้เลือก", " not selected")));
        }
        SavePlanButton.IsEnabled = _loaded && !_saving && _days.Count > 0;
    }

    private async void OnSavePlanClicked(object? sender, EventArgs args)
    {
        if (!_loaded || _saving || _days.Count == 0 || _lifetime is null) return;
        _saving = true; UpdateDays(); ScheduleHost.IsEnabled = false;
        try
        {
            using var lease = _boundary.CreateCancellationLease(_generation, _lifetime.Token);
            var next = T("เริ่มสัปดาห์หน้า (แนะนำ)", "Start next week (recommended)");
            var immediate = T("ใช้ตั้งแต่วันนี้", "Apply from today");
            var choice = await DisplayActionSheetAsync(T("เริ่มใช้แผนใหม่เมื่อไร?", "When should the new plan start?"),
                T("ยกเลิก", "Cancel"), null, next, immediate);
            lease.Token.ThrowIfCancellationRequested();
            if (choice != next && choice != immediate) return;
            var nextWeek = choice == next;
            var today = Today;
            var effective = nextWeek ? TrainingSchedule.Monday(today).AddDays(7) : today;
            var accepted = await DisplayAlertAsync(T("ยืนยันแผนฝึก", "Confirm training plan"),
                T($"{DayNames(_days)} · เริ่ม {effective:d MMM yyyy}\nประวัติและยอดฝึกเดิมไม่เปลี่ยน เป้าหมายสัปดาห์นี้คงเดิม จำนวนวันใหม่มีผลสัปดาห์หน้า",
                    $"{DayNames(_days)} · From {effective:d MMM yyyy}\nYour workout history and totals stay unchanged. This week's goal stays the same; the new count starts next week."),
                T("บันทึก", "Save"), T("ยกเลิก", "Cancel"));
            if (!accepted) return;
            lease.Token.ThrowIfCancellationRequested();
            TrainingSchedule? saved = null;
            if (!await _boundary.TryCommitAsync(_generation, async token =>
                { saved = await _schedules.SaveAsync(today, _days.ToArray(), nextWeek, _viewModel.WeeklyGoal, token); }, lease.Token)) return;
            lease.Token.ThrowIfCancellationRequested();
            _plan = saved!; RenderSchedule();
            await DisplayAlertAsync(T("บันทึกแผนแล้ว", "Plan saved"), T($"เริ่มใช้ {effective:d MMM yyyy}", $"Starts {effective:d MMM yyyy}"), T("ตกลง", "OK"));
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!_boundary.IsCancellationRequested(_generation))
                await DisplayAlertAsync(T("ยังบันทึกไม่ได้", "Could not save"), T("วันฝึกที่เลือกยังอยู่ ลองอีกครั้งได้", "Your selected days are still here. Please try again."), T("ตกลง", "OK"));
        }
        finally { _saving = false; ScheduleHost.IsEnabled = true; UpdateDays(); }
    }

    private void OnReset(object? sender, EventArgs args)
    {
        _lifetime?.Cancel(); _plan = TrainingSchedule.Empty; _days.Clear(); _loaded = false;
        MainThread.BeginInvokeOnMainThread(() => { ScheduleHost.Content = null; SavePlanButton.IsEnabled = false; });
    }
}
