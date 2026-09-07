using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Workout;
using static TrackZ.Mobile.Features.Coach.CoachCopy;

namespace TrackZ.Mobile.Features.Progress;

internal static class ProgressPresentation
{
    internal static bool LargeText
    {
        get
        {
#if IOS
            return UIKit.UIFontMetrics.DefaultMetrics.GetScaledValue(12) > 17;
#elif ANDROID
            return (Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1) > 1.4;
#else
            return false;
#endif
        }
    }
    internal static string BodyName(BodyPart body) => body == BodyPart.Chest ? T("หน้าอก", "Chest") : Body(body);
    internal static string Date(DateOnly day, bool year = false) => day.ToDateTime(TimeOnly.MinValue)
        .ToString(year ? "d MMM yyyy" : "d MMM", CultureInfo.CurrentUICulture);
    internal static string Date(CoachSession s) => Date(DateOnly.FromDateTime(s.At.ToLocalTime().DateTime));
    internal static string Range(MuscleProgressReport report) => $"{Date(report.Start)} – {Date(report.End, true)}";
    internal static string Weight(decimal kg, WeightDisplayUnit unit) =>
        $"{WeightUnitConversion.FromKilograms(kg, unit):0.##} {Unit(unit)}";
    internal static string Unit(WeightDisplayUnit unit) => unit == WeightDisplayUnit.Kilograms ? T("กก.", "kg") : T("ปอนด์", "lb");
    internal static string Load(CoachSession s, WeightDisplayUnit unit) => s.Mode switch
    {
        TrackingMode.Bodyweight => T("น้ำหนักตัว", "Bodyweight"),
        TrackingMode.Assisted => s.AssistedKg is { } kg ? T("แรงช่วย ", "Assistance ") + Weight(kg, unit) : "—",
        _ => s.WeightKg is { } kg ? Weight(kg, unit) : "—"
    };
    internal static string Performance(CoachSession s, WeightDisplayUnit unit) => s.HasUnknownSets
        ? T("ยังเทียบไม่ได้", "Not comparable yet") : $"{Load(s, unit)} × {s.Reps}";
    internal static string Change(ExerciseProgressEvidence e) => e.Change switch
    {
        ProgressChange.MoreReps => T($"ทำได้เพิ่ม {e.Latest!.Reps - e.Before!.Reps} ครั้ง", $"{e.Latest!.Reps - e.Before!.Reps} more reps"),
        ProgressChange.MoreWeight => T("ยกได้หนักขึ้น", "Lifting more weight"),
        ProgressChange.LessAssistance => T("ใช้แรงช่วยน้อยลง", "Using less assistance"),
        ProgressChange.Similar => T("ใกล้เคียงเดิม", "Similar to before"),
        ProgressChange.FewerReps => T("ครั้งล่าสุดทำได้น้อยลง", "Fewer reps this time"),
        _ => T("ข้อมูลยังไม่พอให้เทียบ", "Not enough comparable data")
    };
    internal static string AreaStatus(MuscleProgressArea a) => a.Exercises.Count == 0 ? T("ยังไม่ได้บันทึก", "No sessions logged")
        : a.Improved > 0 ? T($"ดีขึ้น {a.Improved} จาก {a.Exercises.Count} ท่า", $"Improved in {a.Improved} of {a.Exercises.Count} exercises")
        : a.Comparable == 0 ? T("ข้อมูลยังไม่พอ", "Not enough data")
        : a.Exercises.Any(e => e.Change == ProgressChange.FewerReps) ? T("บางท่าทำได้น้อยลง", "Fewer reps in some exercises")
        : T("ใกล้เคียงเดิม", "Similar to before");
    internal static string AreaHint(MuscleProgressArea a) => a.Exercises.Count == 0 ? T("เริ่มฝึกแล้วกลับมาดูได้ที่นี่", "Log a workout to get started")
        : a.Improved > 0 ? a.Exercises.Any(e => e.Change == ProgressChange.MoreReps)
            ? T("น้ำหนักเดิม ทำได้หลายครั้งขึ้น", "More reps at the same weight")
            : T("ดูท่าที่เปลี่ยนแปลง", "See which exercises changed")
        : a.Comparable == 0 ? T("ยังไม่มีท่าเดิมให้เทียบอย่างเหมาะสม", "No comparable sessions yet")
        : T("แตะดูตัวเลขก่อนและล่าสุด", "Compare your earlier and latest sessions");

    internal static Label Label(string text, double size = 14, bool muted = false, bool heading = false)
    {
        var label = new Label { Text = text, FontSize = size,
            FontFamily = heading ? "NotoSansThaiMedium" : "NotoSansThaiRegular", FontAttributes = FontAttributes.None,
            TextColor = Color.FromArgb(muted ? "#A7AFB8" : "#F5F7F8"),
            LineBreakMode = LineBreakMode.WordWrap, VerticalTextAlignment = TextAlignment.Center };
        if (heading) SemanticProperties.SetHeadingLevel(label, SemanticHeadingLevel.Level2);
        return label;
    }
    internal static Label Accent(string text, double size = 14)
    {
        var label = Label(text, size);
        label.TextColor = Application.Current?.Resources.TryGetValue("TrackZPrimary", out var color) == true && color is Color primary
            ? primary : Color.FromArgb("#C8FF3D");
        return label;
    }
    internal static VerticalStackLayout Stack(double spacing, params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = spacing };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    internal static Border Card(View content, Thickness? padding = null)
    {
        var card = new Border { Content = content, Padding = padding ?? new Thickness(14), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 } };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "TrackZSurface");
        card.SetDynamicResource(Border.StrokeProperty, "TrackZBorder");
        return card;
    }
    internal static Button Button(string text, Func<Task> action, bool selected = false)
    {
        var button = new Button { Text = text, FontSize = 14, FontFamily = "NotoSansThaiRegular", FontAttributes = FontAttributes.None, LineBreakMode = LineBreakMode.WordWrap,
            CornerRadius = 10, MinimumHeightRequest = 44, Padding = new Thickness(10, 8),
            BorderWidth = selected ? 0 : 1, BorderColor = Color.FromArgb("#59626C"),
            BackgroundColor = Color.FromArgb(selected ? "#C8FF3D" : "#15191D"),
            TextColor = Color.FromArgb(selected ? "#111609" : "#F5F7F8") };
        button.Clicked += async (_, _) => { if (!button.IsEnabled) return; button.IsEnabled = false;
            try { await action(); } finally { button.IsEnabled = true; } };
        SemanticProperties.SetDescription(button, text + (selected ? T(" เลือกอยู่", " Selected") : ""));
        return button;
    }
    internal static Grid Columns(params View[] children)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < children.Length; i++)
        {
            if (LargeText) { grid.RowDefinitions.Add(new(GridLength.Auto)); grid.RowSpacing = 8; grid.Add(children[i], 0, i); }
            else { grid.ColumnDefinitions.Add(new(GridLength.Star)); grid.Add(children[i], i); }
        }
        return grid;
    }
    internal static View LinkCard(View content, string accessibleName, Func<Task> action)
    {
        // A real button covers the card: native accessibility, focus and pressed feedback.
        var grid = new Grid();
        var button = Button("", action);
        button.BackgroundColor = Colors.Transparent; button.BorderWidth = 0;
        SemanticProperties.SetDescription(button, accessibleName);
        content.InputTransparent = true;
        AutomationProperties.SetExcludedWithChildren(content, true);
        grid.Add(content); grid.Add(button);
        return Card(grid, new Thickness(0));
    }
    internal static string BodyImage(BodyPart body) => "body_" + body.ToString().ToLowerInvariant() + ".png";
}
