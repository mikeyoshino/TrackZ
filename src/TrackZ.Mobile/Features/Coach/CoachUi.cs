using Microsoft.Maui.Controls.Shapes;
using TrackZ.Mobile.Features.Coach;

namespace TrackZ.Mobile.Features.Coach;

internal static class CoachUi
{
    internal static Label Label(string text, double size = 15, bool muted = false, bool heading = false)
    {
        var label = new Label
        {
            Text = text, FontSize = size, FontFamily = heading ? "NotoSansThaiMedium" : "NotoSansThaiRegular",
            LineBreakMode = LineBreakMode.WordWrap, VerticalTextAlignment = TextAlignment.Center
        };
        label.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, muted ? "TrackZTextSecondary" : "TrackZTextPrimary");
        return label;
    }

    internal static Border Card(View content, bool selected = false) => new()
    {
        Content = content, Padding = 16, StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 16 },
        BackgroundColor = Color.FromArgb("#151A1E"),
        Stroke = new SolidColorBrush(Color.FromArgb(selected ? "#C8FF3D" : "#2A3036"))
    };

    internal static Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, FontFamily = "NotoSansThaiMedium", FontSize = 15,
            CornerRadius = 16, MinimumHeightRequest = 48, Padding = new Thickness(14, 10),
            BackgroundColor = Color.FromArgb(primary ? "#C8FF3D" : "#20262A"),
            TextColor = Color.FromArgb(primary ? "#0B1005" : "#F5F6F6")
        };
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            try { await action(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    internal static VerticalStackLayout Stack(params View[] children)
    {
        var stack = new VerticalStackLayout { Spacing = 12 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
}
