using Microsoft.Maui.Controls.Shapes;
using static TrackZ.Mobile.Features.History.HistoryPresentation;

namespace TrackZ.Mobile.Features.History;

internal static class HistoryCalendarViews
{
    internal static Label Label(string text, double size = 14, bool muted = false)
    {
        var label = new Label { Text = text, FontFamily = "NotoSansThaiRegular", FontSize = size,
            VerticalTextAlignment = TextAlignment.Center };
        label.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty,
            muted ? "TrackZTextSecondary" : "TrackZTextPrimary");
        return label;
    }

    internal static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, FontFamily = "NotoSansThaiRegular", FontSize = 14, FontAttributes = FontAttributes.None,
            Padding = new Thickness(12, 8), MinimumHeightRequest = 44,
            CornerRadius = 12, BorderWidth = 0, BackgroundColor = Colors.Transparent
        };
        button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty,
            primary ? "TrackZPrimaryContrast" : "TrackZTextPrimary");
        if (primary) Background(button, "TrackZPrimary");
        button.Clicked += (_, _) => action();
        return button;
    }

    internal static void Background(VisualElement view, string resource)
    {
        // A literal local Transparent value takes precedence over a dynamic
        // resource in MAUI; remove it before applying the selected color.
        view.ClearValue(VisualElement.BackgroundColorProperty);
        view.SetDynamicResource(VisualElement.BackgroundColorProperty, resource);
    }

    internal static Border Card(View content, double padding = 12)
    {
        var card = new Border { Content = content, Padding = padding,
            StrokeShape = new RoundRectangle { CornerRadius = 16 }, StrokeThickness = 1 };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "TrackZSurface");
        card.SetDynamicResource(Border.StrokeProperty, "TrackZBorder");
        return card;
    }

    internal static VerticalStackLayout Stack(params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 12 };
        foreach (var child in children) stack.Add(child);
        return stack;
    }

    internal static View Calendar(HistoryCalendarState state, Action openMonth,
        Action<DateOnly> selectDate, out Button? selectedButton)
    {
        selectedButton = null;
        var navigation = new Grid
        {
            ColumnDefinitions = [new(44), new(GridLength.Star), new(44), new(GridLength.Auto)], ColumnSpacing = 4
        };
        var previous = Button("‹", () => state.MoveMonth(-1));
        previous.IsEnabled = state.CanPreviousMonth;
        SemanticProperties.SetDescription(previous, T("เดือนก่อนหน้า", "Previous month"));
        var month = Button(state.MonthTitle + "  ⌄", openMonth);
        SemanticProperties.SetDescription(month, T("เลือกเดือนและปี", "Choose month and year") + ", " + state.MonthTitle);
        var next = Button("›", () => state.MoveMonth(1));
        next.IsEnabled = state.CanNextMonth;
        next.Opacity = next.IsEnabled ? 1 : 0.35;
        SemanticProperties.SetDescription(next, T("เดือนถัดไป", "Next month"));
        var today = Button(T("วันนี้", "Today"), state.GoToToday);
        today.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, "TrackZPrimary");
        navigation.Add(previous); navigation.Add(month, 1); navigation.Add(next, 2); navigation.Add(today, 3);

        var grid = new Grid { RowSpacing = 4, ColumnSpacing = 0 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new(GridLength.Star));
        grid.RowDefinitions.Add(new(GridLength.Auto));
        var weekdays = Thai ? new[] { "จ.", "อ.", "พ.", "พฤ.", "ศ.", "ส.", "อา." }
            : new[] { "M", "T", "W", "T", "F", "S", "S" };
        for (var i = 0; i < 7; i++)
        {
            var label = Label(weekdays[i], 12, true);
            label.HorizontalTextAlignment = TextAlignment.Center;
            grid.Add(label, i);
        }
        for (var i = 0; i < state.Days.Count / 7; i++) grid.RowDefinitions.Add(new(GridLength.Auto));
        for (var i = 0; i < state.Days.Count; i++)
        {
            var day = state.Days[i];
            if (day.Date is not { } date) continue;
            var cell = new Grid { MinimumHeightRequest = 48 };
            var button = Button(date.Day.ToString(), () => selectDate(date));
            button.Padding = 0;
            button.WidthRequest = button.HeightRequest = 44;
            button.HorizontalOptions = LayoutOptions.Center;
            button.VerticalOptions = LayoutOptions.Center;
            button.CornerRadius = 22;
            button.BorderWidth = !day.IsSelected && (day.IsToday || day.IsPlanned) ? 1.5 : 0;
            button.SetDynamicResource(Microsoft.Maui.Controls.Button.BorderColorProperty,
                day.IsToday ? "TrackZPrimary" : "TrackZTextSecondary");
            if (day.IsSelected || day.IsPastWithoutTraining)
                Background(button, day.IsSelected ? "TrackZPrimary" : "TrackZDisabledSurface");
            button.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty,
                day.IsSelected ? "TrackZPrimaryContrast" : date > state.Today ? "TrackZTextSecondary" : "TrackZTextPrimary");
            var status = day.HasMatchingTraining ? T("มีการฝึก", "Workout recorded")
                : day.HasTraining ? T("มีการฝึกที่ไม่ตรงกับตัวกรอง", "Workout outside current filter")
                : date > state.Today ? T("วันที่ยังมาไม่ถึง", "Future date") : T("ไม่มีการฝึกที่บันทึกไว้", "No workout recorded");
            SemanticProperties.SetDescription(button, $"{date:d MMMM yyyy}, {status}"
                + (day.IsToday ? T(", วันนี้", ", today") : "")
                + (day.IsPlanned ? T(", วางแผนฝึกไว้", ", planned training") : "")
                + (day.IsSelected ? T(", เลือกอยู่", ", selected") : ""));
            cell.Add(button);
            if (day.IsSelected) selectedButton = button;
            if (day.HasTraining)
            {
                var dot = new Border { WidthRequest = 4, HeightRequest = 4, StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 2 },
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End,
                    InputTransparent = true };
                dot.SetDynamicResource(VisualElement.BackgroundColorProperty,
                    day.HasMatchingTraining ? "TrackZPrimary" : "TrackZTextSecondary");
                AutomationProperties.SetExcludedWithChildren(dot, true);
                cell.Add(dot);
            }
            grid.Add(cell, i % 7, i / 7 + 1);
        }
        var legend = new FlexLayout { Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var trained = Label("● " + T("มีการฝึก", "Trained"), 12);
        trained.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "TrackZPrimary");
        trained.Margin = new Thickness(0, 0, 16, 0);
        legend.Add(trained);
        legend.Add(Label("● " + T("ไม่ได้ฝึก", "No training"), 12, true));
        if (state.HasFilter) legend.Add(Label("   • " + T("นอกตัวกรอง", "Outside filter"), 12, true));
        if (state.HasSchedule) legend.Add(Label("   ◯ " + T("วางแผนไว้", "Planned"), 12, true));
        var summary = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        summary.Add(Label(state.HasFilter ? T("เดือนนี้ · ตามตัวกรอง", "Month · filtered") : T("เดือนนี้", "This month"), 13));
        summary.Add(Label(T($"{state.MonthWorkoutCount} ครั้ง  ·  {state.MonthSetCount} เซ็ต",
            $"{state.MonthWorkoutCount} workouts  ·  {state.MonthSetCount} sets"), 14), 1);
        return Stack(navigation, Card(Stack(grid, legend), 8), Card(summary));
    }
}
