using TrackZ.Domain.Exercises;
using static TrackZ.Mobile.Features.History.HistoryPresentation;
using static TrackZ.Mobile.Features.History.HistoryCalendarViews;

namespace TrackZ.Mobile.Features.History;

internal abstract class HistoryCalendarSheet : ContentPage
{
    protected VerticalStackLayout Body { get; } = new() { Spacing = 16 };
    protected Grid Heading { get; } = new() { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
    private readonly Button _apply;
    private readonly Button _cancel;
    private readonly Func<object?, Task> _complete;
    private bool _closing;

    protected HistoryCalendarSheet(string title, Func<object?, Task> complete)
    {
        _complete = complete;
        SetDynamicResource(BackgroundColorProperty, "TrackZSurface");
        SafeAreaEdges = SafeAreaEdges.All;
        Heading.Add(Label(title, 20));
        _apply = Button(T("แสดงผล", "Apply"), () => _ = FinishAsync(Value), true);
        _cancel = Button(T("ยกเลิก", "Cancel"), () => _ = FinishAsync(null));
        var root = new Grid { Padding = new Thickness(20, 24, 20, 12), RowSpacing = 16,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        root.Add(Heading);
        root.Add(new ScrollView { Content = Body }, 0, 1);
        root.Add(Stack(_apply, _cancel), 0, 2);
        Content = root;
    }

    protected abstract object Value { get; }
    private async Task FinishAsync(object? value)
    {
        if (_closing) return;
        _closing = true;
        _apply.IsEnabled = _cancel.IsEnabled = false;
        try { await _complete(value); }
        catch
        {
            _closing = false;
            _apply.IsEnabled = _cancel.IsEnabled = true;
            await DisplayAlertAsync(T("ปิดหน้าต่างไม่สำเร็จ", "Could not close"),
                T("กรุณาลองอีกครั้ง", "Please try again"), T("ตกลง", "OK"));
        }
    }
    protected override bool OnBackButtonPressed()
    {
        _ = FinishAsync(null);
        return true;
    }
}

internal sealed class HistoryFilterSheet : HistoryCalendarSheet
{
    private readonly HashSet<BodyPart> _selected;
    private readonly Dictionary<BodyPart, Button> _buttons = [];
    private readonly Grid _options = new() { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)],
        RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
        ColumnSpacing = 12, RowSpacing = 12 };

    internal HistoryFilterSheet(IReadOnlySet<BodyPart> selected, Func<object?, Task> complete)
        : base(T("ตัวกรอง", "Filters"), complete)
    {
        _selected = selected.ToHashSet();
        var clear = Button(T("ล้างค่า", "Clear"), () => { _selected.Clear(); Render(); });
        clear.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, "TrackZPrimary");
        Heading.Add(clear, 1);
        Body.Add(Label(T("ดูเฉพาะส่วนร่างกายที่ต้องการ", "Show the muscle groups you want"), 14, true));
        Body.Add(Label(T("เลือกได้มากกว่าหนึ่งส่วน", "Choose more than one"), 13, true));
        Body.Add(_options);
        Body.Add(Label(T("ปฏิทินและรายการจะแสดงเฉพาะการฝึกที่ตรงกัน โดยนับเซ็ตทั้งหมดของการฝึกนั้น",
            "Calendar and list show matching workouts, counting all sets in each workout."), 13, true));
        Render();
    }
    protected override object Value => _selected.ToArray();

    private void Render()
    {
        var parts = Enum.GetValues<BodyPart>();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var selected = _selected.Contains(part);
            if (!_buttons.TryGetValue(part, out var button))
            {
                button = Button("", () =>
                {
                    if (!_selected.Remove(part)) _selected.Add(part);
                    Render();
                    if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid())
                        SemanticScreenReader.Announce(HistoryPresentation.Body(part)
                            + (_selected.Contains(part) ? T(" เลือกแล้ว", " selected") : T(" ยกเลิกแล้ว", " deselected")));
                });
                button.HeightRequest = 56;
                button.BorderWidth = 1;
                HistoryCalendarViews.Background(button, "TrackZSurfaceRaised");
                _buttons.Add(part, button);
                _options.Add(button, i % 2, i / 2);
            }
            button.Text = HistoryPresentation.Body(part) + (selected ? "   ✓" : "   ○");
            button.SetDynamicResource(Microsoft.Maui.Controls.Button.BorderColorProperty, selected ? "TrackZPrimary" : "TrackZBorder");
            SemanticProperties.SetDescription(button, HistoryPresentation.Body(part)
                + (selected ? T(", เลือกอยู่", ", selected") : T(", ไม่ได้เลือก", ", not selected")));
        }
    }
}

internal sealed class HistoryMonthSheet : HistoryCalendarSheet
{
    private readonly DateOnly _today;
    private DateOnly _selected;
    private int _year;
    private readonly VerticalStackLayout _months = new() { Spacing = 12 };

    internal HistoryMonthSheet(DateOnly month, DateOnly today, Func<object?, Task> complete)
        : base(T("เลือกเดือนและปี", "Choose month and year"), complete)
    {
        _selected = month; _today = today; _year = month.Year;
        Body.Add(_months);
        Render();
    }
    protected override object Value => _selected;

    private void Render()
    {
        _months.Clear();
        var header = new Grid { ColumnDefinitions = [new(44), new(GridLength.Star), new(44)] };
        var previous = Button("‹", () => { _year--; Render(); });
        previous.IsEnabled = _year > 1;
        SemanticProperties.SetDescription(previous, T("ปีก่อนหน้า", "Previous year"));
        var year = Label(new DateOnly(_year, 1, 1).ToString("yyyy"), 20);
        year.HorizontalTextAlignment = TextAlignment.Center;
        var next = Button("›", () => { _year++; Render(); });
        next.IsEnabled = _year < _today.Year;
        SemanticProperties.SetDescription(next, T("ปีถัดไป", "Next year"));
        header.Add(previous); header.Add(year, 1); header.Add(next, 2);
        _months.Add(header);
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)],
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8, RowSpacing = 8 };
        for (var month = 1; month <= 12; month++)
        {
            var date = new DateOnly(_year, month, 1);
            var selected = date == _selected;
            var button = Button(date.ToString("MMM") + (selected ? " ✓" : ""), () => { _selected = date; Render(); }, selected);
            button.IsEnabled = date <= new DateOnly(_today.Year, _today.Month, 1);
            button.Opacity = button.IsEnabled ? 1 : 0.35;
            SemanticProperties.SetDescription(button, date.ToString("MMMM yyyy")
                + (selected ? T(", เลือกอยู่", ", selected") : ""));
            grid.Add(button, (month - 1) % 3, (month - 1) / 3);
        }
        _months.Add(grid);
        _months.Add(Label(T("เลือกไว้: ", "Selected: ") + _selected.ToString("MMMM yyyy"), 13, true));
    }
}
