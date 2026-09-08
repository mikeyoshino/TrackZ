using TrackZ.Domain.Exercises;
using TrackZ.Domain.Muscles;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using static TrackZ.Mobile.Features.Coach.CoachCopy;
using static TrackZ.Mobile.Features.Progress.ProgressPresentation;

namespace TrackZ.Mobile.Features.Progress;

/// <summary>One projection and selection state for overview, body group and region drilldown.</summary>
public sealed class MuscleCoveragePage : ContentPage, IQueryAttributable
{
    private readonly MuscleCoverageSource _source;
    private readonly IAccountSessionBoundary _boundary;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _zone;
    private readonly WorkoutViewModel _workout;
    private readonly IExerciseThumbnailCache _images;
    private readonly ExerciseTechniqueCatalog _techniques;
    private readonly VerticalStackLayout _content = new() { Padding = new Thickness(20, 12, 20, 24), Spacing = 16 };
    private readonly ScrollView _scroll;
    private CancellationTokenSource? _lifetime;
    private AccountSessionGeneration _generation;
    private MuscleCoverageSnapshot? _snapshot;
    private DateOnly _week;
    private BodyPart? _group;
    private string? _region;
    private Guid? _selected;
    private ExerciseEquipment? _equipment;
    private bool _showAllRecommendations;
    private bool _subscribed;
    private Guid? _requestedExercise;
    private readonly Dictionary<Guid, string> _artwork = [];
    private MuscleSvgDocument? _diagram;
    private bool _adding;

    public MuscleCoveragePage(MuscleCoverageSource source, IAccountSessionBoundary boundary, IClock clock,
        TimeZoneInfo zone, WorkoutViewModel workout, IExerciseThumbnailCache images, ExerciseTechniqueCatalog techniques)
    {
        _source = source; _boundary = boundary; _clock = clock; _zone = zone;
        _workout = workout; _images = images; _techniques = techniques;
        _generation = boundary.Capture();
        _week = CurrentWeek;
        BackgroundColor = Color.FromArgb("#090D0E");
        Shell.SetNavBarIsVisible(this, false);
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        _scroll = new ScrollView { Content = _content };
        Content = _scroll;
        Title = T("ผลการฝึก", "Training results");
    }

    private DateOnly CurrentWeek
    {
        get { var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, _zone).DateTime);
            return today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); }
    }
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("exerciseId", out var value) && Guid.TryParse(Convert.ToString(value), out var id))
            _requestedExercise = id;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_subscribed) { _boundary.SessionReset += Reset; _subscribed = true; }
        await LoadAsync();
        if (_requestedExercise is { } id && _snapshot is not null)
        { _requestedExercise = null; await Shell.Current.GoToAsync($"{nameof(ExerciseProgressPage)}?exerciseId={id:D}"); }
    }
    protected override void OnDisappearing()
    {
        _lifetime?.Cancel();
        if (_subscribed) { _boundary.SessionReset -= Reset; _subscribed = false; }
        base.OnDisappearing();
    }
    private void Reset(object? sender, EventArgs args)
    {
        _lifetime?.Cancel();
        Dispatcher.Dispatch(() => { _snapshot = null; _selected = null; _group = null; _region = null; _showAllRecommendations = false; _artwork.Clear(); _content.Clear(); });
    }
    private async Task LoadAsync()
    {
        _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = new();
        if (_boundary.IsCancellationRequested(_generation))
        { _snapshot = null; _selected = null; _group = null; _region = null; _artwork.Clear(); _week = CurrentWeek; }
        _generation = _boundary.Capture();
        using var lease = _boundary.CreateCancellationLease(_generation, _lifetime.Token);
        _content.Clear();
        _content.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#C8FF3D"), HeightRequest = 60 });
        try
        {
            var diagramTask = LoadDiagramAsync(lease.Token);
            var snapshot = await _source.LoadAsync(_week, lease.Token);
            var diagram = await diagramTask;
            lease.Token.ThrowIfCancellationRequested();
            _snapshot = snapshot;
            _diagram = diagram;
            if (_selected is { } selected && snapshot.ActiveExerciseIds.Contains(selected)) _selected = null;
            Render();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (lease.Token.IsCancellationRequested) return;
            _content.Clear();
            _content.Add(Label(T("ยังโหลดผลการฝึกไม่ได้", "Could not load your training"), 18));
            _content.Add(Button(T("ลองอีกครั้ง", "Try again"), LoadAsync, true));
        }
    }

    private static async Task<MuscleSvgDocument?> LoadDiagramAsync(CancellationToken cancellationToken)
    {
        try { return await MuscleSvgDocument.LoadAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
    private void Render()
    {
        if (_snapshot is null || _boundary.IsCancellationRequested(_generation)) return;
        _content.Clear();
        Shell.SetTabBarIsVisible(this, _region is null);
        var report = _snapshot.Report;
        var name = _region is { } id ? Name(report.Regions.Single(r => r.Id == id))
            : _group is { } group ? GroupName(group) : T("ผลการฝึก", "Training results");
        var header = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(44)] };
        if (_group is not null) header.Add(IconButton("‹", T("ย้อนกลับ", "Back"), BackAsync));
        var title = Label(name, 23, heading: true); title.HorizontalTextAlignment = TextAlignment.Center;
        header.Add(title, 1); _content.Add(header);
        if (_region is not null) { RenderRegion(); return; }
        var dates = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(44)] };
        var previous = IconButton("‹", T("สัปดาห์ก่อน", "Previous week"), () => MoveWeekAsync(-7)); previous.IsEnabled = _week > new DateOnly(2000, 1, 3); dates.Add(previous);
        var range = Label($"{Date(report.Start)} – {Date(report.End)}", 14, true);
        range.HorizontalTextAlignment = TextAlignment.Center; dates.Add(range, 1);
        var next = IconButton("›", T("สัปดาห์ถัดไป", "Next week"), () => MoveWeekAsync(7)); next.IsEnabled = _week < CurrentWeek; dates.Add(next, 2);
        _content.Add(dates);
        if (_group is null)
        {
            _content.Add(Label(T("สัปดาห์นี้ฝึกส่วนไหนบ้าง", "Which muscles did you train?"), 18));
            _content.Add(Diagram(DiagramHeight(_group, _region)));
            _content.Add(Legend());
            var groups = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], RowSpacing = 8, ColumnSpacing = 8 };
            var all = Enum.GetValues<BodyPart>();
            for (var i = 0; i < all.Length; i++)
            {
                if (i % 2 == 0) groups.RowDefinitions.Add(new(GridLength.Auto));
                var targetGroup = all[i];
                groups.Add(QuietButton(GroupName(targetGroup) + "  ›", () => { _group = targetGroup; return ChangedAsync(); }), i % 2, i / 2);
            }
            _content.Add(groups);
            _content.Add(Label(T("อิงจากท่าและเซ็ตที่บันทึก", "Based on recorded exercises and sets"), 12, true));
            _content.Add(Button(T("ดูน้ำหนักและจำนวนครั้งที่เปลี่ยนไป", "Compare your weights and reps"),
                () => Shell.Current.GoToAsync(nameof(ExerciseProgressPage))));
        }
        else
        {
            _content.Add(Diagram(DiagramHeight(_group, _region)));
            var rows = Stack(6);
            foreach (var region in report.Regions.Where(r => r.BodyPart == _group))
            {
                var statusText = Status(region) + (region.PrimarySets > 0 && region.SecondarySets > 0
                    ? T($"\nช่วยอีก {region.SecondarySets} เซ็ต", $"\n{region.SecondarySets} secondary sets") : "");
                var row = RegionRowContent(Name(region), statusText, StatusTextColor(region.Status), LargeText);
                rows.Add(LinkCard(row, Name(region) + ", " + Status(region), () =>
                { _region = region.Id; _selected = null; _equipment = null; _showAllRecommendations = false; return ChangedAsync(); }));
            }
            _content.Add(rows);
            _content.Add(Label(T("ไม่จำเป็นต้องฝึกครบทุกส่วนในสัปดาห์เดียว", "You do not need to train every region each week"), 12, true));
        }
        if (report.UnclassifiedSets + report.UnmappedSets > 0)
            _content.Add(Label(T($"อีก {report.UnclassifiedSets + report.UnmappedSets} เซ็ตยังจัดกลุ่มไม่ได้", $"{report.UnclassifiedSets + report.UnmappedSets} sets cannot yet be classified"), 12, true));
    }
    private void RenderRegion()
    {
        var snapshot = _snapshot!;
        var region = snapshot.Report.Regions.Single(r => r.Id == _region);
        if (region.Id == "deep-core")
        {
            var layer = Label(T("ภาพตัดขวาง · ชั้นกล้ามเนื้อลึก", "Cross-section · Deep muscle layer"), 13, true);
            layer.HorizontalTextAlignment = TextAlignment.Center;
            _content.Add(layer);
        }
        _content.Add(Diagram(DiagramHeight(_group, _region)));
        var status = Label(Status(region), 17); status.HorizontalTextAlignment = TextAlignment.Center;
        status.TextColor = StatusTextColor(region.Status); _content.Add(status);
        if (region.PrimarySets == 0)
            _content.Add(Label(T("ยังไม่มีท่าที่เน้นส่วนนี้ในช่วงที่เลือก", "No primary exercise recorded in this period"), 13, true));
        else if (region.SecondarySets > 0)
            _content.Add(Label(T($"ช่วยในท่าอื่นอีก {region.SecondarySets} เซ็ต", $"Also helped in {region.SecondarySets} other sets"), 13, true));
        _content.Add(Label(T("เลือกท่าที่เน้นส่วนนี้", "Choose an exercise for this region"), 18));
        var filter = new Picker { Title = T("อุปกรณ์ทั้งหมด", "All equipment"), TextColor = Colors.White,
            FontFamily = "NotoSansThaiRegular", FontSize = 14, MinimumHeightRequest = 44,
            BackgroundColor = Color.FromArgb("#15191D"), HorizontalOptions = LayoutOptions.Fill };
        filter.Items.Add(T("อุปกรณ์ทั้งหมด", "All equipment"));
        foreach (var value in Enum.GetValues<ExerciseEquipment>()) filter.Items.Add(value.Label(Thai));
        filter.SelectedIndex = _equipment is { } eq ? (int)eq + 1 : 0;
        filter.SelectedIndexChanged += (_, _) => { _equipment = filter.SelectedIndex <= 0 ? null : (ExerciseEquipment)(filter.SelectedIndex - 1); _selected = null; _showAllRecommendations = false; Render(); };
        _content.Add(filter);
        var ids = MuscleCatalog.Recommendations(region.Id).ToHashSet();
        var candidates = snapshot.Exercises.Where(e => !e.IsCustom && ids.Contains(e.Id)
                && !snapshot.ActiveExerciseIds.Contains(e.Id) && !_workout.Exercises.Any(x => x.ExerciseDefinitionId == e.Id)
                && (_equipment is null || Equipment(e) == _equipment))
            .OrderBy(e => e.Name).ToArray();
        foreach (var exercise in candidates.Take(VisibleRecommendationCount(candidates.Length, _showAllRecommendations)))
        {
            var row = new Grid { ColumnDefinitions = [new(72), new(GridLength.Star), new(44)], ColumnSpacing = 12, Padding = 10 };
            var image = RecommendationThumbnail(exercise.ThumbnailUri ?? _artwork.GetValueOrDefault(exercise.Id) ?? "exercise_placeholder.png");
            SemanticProperties.SetDescription(image, T("ดูภาพและวิธีฝึก ", "View image and tips for ") + exercise.Name);
            image.Clicked += async (_, _) => await PreviewAsync(exercise);
            row.Add(image);
            row.Add(Stack(4, Label(exercise.Name, 15), Label(Equipment(exercise).Label(Thai), 12, true)), 1);
            var select = RecommendationSelector(_selected == exercise.Id, _selected == exercise.Id
                ? T("เลือกอยู่: ", "Selected: ") + exercise.Name
                : T("เลือกท่า ", "Select exercise ") + exercise.Name,
                () => { _selected = exercise.Id; Render(); return Task.CompletedTask; });
            row.Add(select, 2);
            _content.Add(Card(row, new Thickness(0)));
            _ = LoadImageAsync(image, exercise);
        }
        if (candidates.Length == 0)
            _content.Add(Label(T("ยังไม่มีท่าที่เพิ่มได้สำหรับตัวกรองนี้", "No available exercises for this filter"), 14, true));
        else if (candidates.Length > 2)
            _content.Add(QuietButton(
                _showAllRecommendations ? T("แสดงน้อยลง", "Show fewer") : T("ดูท่าอื่น", "See other exercises"),
                () => { _showAllRecommendations = !_showAllRecommendations; Render(); return Task.CompletedTask; }));
        if (_adding) _content.Add(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#C8FF3D") });
        var add = AddExerciseButton(_selected is not null, _adding, AddAsync);
        _content.Add(add);
        _content.Add(Label(T("เลือกตามอุปกรณ์ที่มี", "Choose equipment available to you"), 12, true));
    }
    private static ExerciseEquipment Equipment(CachedExercise e) =>
        e.Name is "Seated Leg Curl" or "Lying Leg Curl" or "Leg Extension" or "Leg Press" or "Seated Calf Raise" or "Standing Calf Raise"
            ? ExerciseEquipment.Machine : ExerciseEquipmentLabels.Identify(e);
    private async Task LoadImageAsync(ImageButton image, CachedExercise exercise)
    {
        if (exercise.ThumbnailUri is not null || _lifetime is null) return;
        using var lease = _boundary.CreateCancellationLease(_generation, _lifetime.Token);
        try { var path = await _images.CacheAsync(exercise.RemoteThumbnailRoute, lease.Token);
            lease.Token.ThrowIfCancellationRequested(); if (path is not null) { _artwork[exercise.Id] = path; image.Source = path; } }
        catch (OperationCanceledException) { } catch { /* A missing thumbnail must not remove the exercise. */ }
    }
    private async Task PreviewAsync(CachedExercise exercise)
    {
        var tip = _techniques.Get(exercise.Name, exercise.BodyPart, exercise.IsCustom, System.Globalization.CultureInfo.CurrentUICulture);
        var page = new ContentPage { BackgroundColor = BackgroundColor, Title = exercise.Name, SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All };
        var close = Button(T("กลับ", "Back"), () => Navigation.PopModalAsync());
        var image = new Image { Source = exercise.ThumbnailUri ?? _artwork.GetValueOrDefault(exercise.Id) ?? "exercise_placeholder.png", Aspect = Aspect.AspectFit, HeightRequest = 300 };
        var content = Stack(16, close, Label(exercise.Name, 20), image,
            Label(string.Join("\n", tip.Steps), 16), Label(tip.Tip, 14), Label(tip.Caution, 13, true));
        content.Padding = 24;
        page.Content = new ScrollView { Content = content };
        await Navigation.PushModalAsync(page);
    }
    private async Task AddAsync()
    {
        if (_selected is not { } id || _lifetime is null || _adding) return;
        using var lease = _boundary.CreateCancellationLease(_generation, _lifetime.Token);
        _adding = true; Render();
        try
        {
            await _workout.RestoreAsync(lease.Token);
            lease.Token.ThrowIfCancellationRequested();
            await _workout.AddExercisesAsync([id], lease.Token);
            lease.Token.ThrowIfCancellationRequested();
            _selected = null;
            await Shell.Current.GoToAsync("//train");
        }
        catch (OperationCanceledException) { }
        catch { if (!lease.Token.IsCancellationRequested) await DisplayAlertAsync(T("ยังเพิ่มท่าไม่ได้", "Could not add exercise"), T("ลองอีกครั้ง ข้อมูลเดิมยังอยู่", "Please try again. Your records are safe."), T("ตกลง", "OK")); }
        finally { _adding = false; if (!lease.Token.IsCancellationRequested) Render(); }
    }
    private View Diagram(double height)
    {
        if (_diagram is null)
            return Card(Label(T("ยังแสดงแผนภาพไม่ได้ ดูสถานะจากรายการด้านล่าง", "Diagram unavailable. Use the status list below."), 13, true));
        var view = new GraphicsView { HeightRequest = height, Drawable = new MuscleBodyDiagram(_snapshot!.Report, _diagram, _group, _region) };
        SemanticProperties.SetDescription(view, string.Join("; ", _snapshot.Report.Regions
            .Where(r => _region is not null ? r.Id == _region : _group is null || r.BodyPart == _group)
            .Select(r => Name(r) + ": " + Status(r))));
        view.EndInteraction += async (_, args) =>
        {
            if (args.Touches.Length == 0 || _adding) return;
            var id = MuscleBodyDiagram.HitTest(_diagram, args.Touches[0], new RectF(0, 0, (float)view.Width, (float)view.Height), _group, _region);
            if (id is null) return;
            _group = MuscleCatalog.Regions.Single(r => r.Id == id).BodyPart;
            _region = id; _selected = null; _equipment = null;
            await ChangedAsync();
        };
        return view;
    }
    private static View Legend() => new FlexLayout { JustifyContent = Microsoft.Maui.Layouts.FlexJustify.SpaceBetween,
        Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = {
            LegendItem(MuscleTrainingStatus.Primary, T("ฝึกหลัก", "Primary")),
            LegendItem(MuscleTrainingStatus.Secondary, T("ช่วยในท่าอื่น", "Secondary")),
            LegendItem(MuscleTrainingStatus.NoRecord, T("ยังไม่มีบันทึก", "No record")) } };
    private static View LegendItem(MuscleTrainingStatus status, string text)
    {
        var label = Label("● " + text, 12); label.TextColor = StatusTextColor(status); return label;
    }
    internal static Color StatusTextColor(MuscleTrainingStatus status) => status == MuscleTrainingStatus.NoRecord
        ? Color.FromArgb("#A7AFB8")
        : MuscleBodyDiagram.StatusColor(status);
    private static string Name(MuscleRegionCoverage r) => Thai ? r.ThaiName : r.EnglishName;
    private static string GroupName(BodyPart group) => group == BodyPart.Legs ? T("ขาและสะโพก", "Legs and hips") : BodyName(group);
    private static string Status(MuscleRegionCoverage r) => r.Status switch
    {
        MuscleTrainingStatus.Primary => T($"ฝึกหลัก {r.PrimarySets} เซ็ต", $"{r.PrimarySets} primary sets"),
        MuscleTrainingStatus.Secondary => T($"ช่วยในท่าอื่น {r.SecondarySets} เซ็ต", $"{r.SecondarySets} secondary sets"),
        _ => T("ยังไม่มีบันทึก", "No record")
    };
    internal static int VisibleRecommendationCount(int available, bool expanded) =>
        Math.Min(Math.Max(available, 0), expanded ? Math.Max(available, 0) : 2);

    internal static double DiagramHeight(BodyPart? selectedGroup, string? selectedRegion) =>
        selectedRegion is not null ? 190 : selectedGroup is not null ? 185 : 320;

    internal static Grid RegionRowContent(string name, string statusText, Color statusColor, bool largeText)
    {
        var row = new Grid { ColumnSpacing = 6, Padding = 12, MinimumHeightRequest = 48 };
        var nameLabel = Label(name, 14);
        var status = Label(statusText, 13);
        status.TextColor = statusColor;
        var chevron = Label("›", 20, true);
        if (largeText)
        {
            row.ColumnDefinitions.Add(new(GridLength.Star));
            row.ColumnDefinitions.Add(new(14));
            row.RowDefinitions.Add(new(GridLength.Auto));
            row.RowDefinitions.Add(new(GridLength.Auto));
            row.RowSpacing = 2;
            row.Add(nameLabel);
            row.Add(status, 0, 1);
            row.Add(chevron, 1, 0);
            Grid.SetRowSpan(chevron, 2);
        }
        else
        {
            row.ColumnDefinitions.Add(new(GridLength.Star));
            row.ColumnDefinitions.Add(new(GridLength.Auto));
            row.ColumnDefinitions.Add(new(14));
            row.Add(nameLabel);
            row.Add(status, 1);
            row.Add(chevron, 2);
        }
        return row;
    }

    internal static ImageButton RecommendationThumbnail(ImageSource source) => new()
    {
        Source = source,
        Aspect = Aspect.AspectFill,
        HeightRequest = 72,
        WidthRequest = 72,
        CornerRadius = 10,
        BackgroundColor = Color.FromArgb("#252C31")
    };

    internal static Microsoft.Maui.Controls.Button RecommendationSelector(bool selected, string accessibleName, Func<Task> action)
    {
        var button = Button(selected ? "✓" : "○", action, selected);
        button.BackgroundColor = Colors.Transparent;
        button.BorderWidth = 0;
        button.FontSize = 24;
        button.Padding = 0;
        button.MinimumWidthRequest = 44;
        button.MinimumHeightRequest = 44;
        button.WidthRequest = 44;
        button.HeightRequest = 44;
        button.HorizontalOptions = LayoutOptions.Center;
        button.VerticalOptions = LayoutOptions.Center;
        button.TextColor = Color.FromArgb(selected ? "#C8FF3D" : "#F5F7F8");
        SemanticProperties.SetDescription(button, accessibleName);
        return button;
    }

    internal static Microsoft.Maui.Controls.Button AddExerciseButton(bool hasSelection, bool adding, Func<Task> action)
    {
        var button = Button(adding ? T("กำลังเพิ่ม…", "Adding…") : T("เพิ่มท่านี้", "Add exercise"), action, true);
        button.MinimumHeightRequest = 52;
        button.FontFamily = "NotoSansThaiMedium";
        button.IsEnabled = hasSelection && !adding;
        if (!button.IsEnabled)
        {
            button.BackgroundColor = Color.FromArgb("#252B31");
            button.TextColor = Color.FromArgb("#68717B");
        }
        return button;
    }

    private static Microsoft.Maui.Controls.Button IconButton(string icon, string accessibleName, Func<Task> action)
    {
        var button = Button(icon, action);
        button.BackgroundColor = Colors.Transparent;
        button.BorderWidth = 0;
        button.FontSize = 26;
        button.Padding = 0;
        button.MinimumWidthRequest = 44;
        button.WidthRequest = 44;
        SemanticProperties.SetDescription(button, accessibleName);
        return button;
    }

    private static Microsoft.Maui.Controls.Button QuietButton(string text, Func<Task> action)
    {
        var button = Button(text, action);
        button.BackgroundColor = Color.FromArgb("#15191D");
        button.BorderWidth = 0;
        return button;
    }
    private async Task MoveWeekAsync(int days)
    {
        if (days is not (-7 or 7)) return;
        var target = _week.AddDays(days);
        if (target < new DateOnly(2000, 1, 3) || target > CurrentWeek) return;
        _week = target; _selected = null; await LoadAsync();
    }
    private async Task ChangedAsync() { Render(); await _scroll.ScrollToAsync(0, 0, false); }
    private Task BackAsync() { if (_region is not null) { _region = null; _selected = null; _showAllRecommendations = false; } else _group = null; return ChangedAsync(); }
    protected override bool OnBackButtonPressed() { if (_group is null) return base.OnBackButtonPressed(); _ = BackAsync(); return true; }
}
