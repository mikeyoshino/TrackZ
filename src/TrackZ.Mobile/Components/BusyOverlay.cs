using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace TrackZ.Mobile.Components;

/// <summary>Visible, input-blocking feedback for an operation already in progress.</summary>
public sealed class BusyOverlay : ContentView
{
    public BusyOverlay()
    {
        IsVisible = false;
        ZIndex = 100;
        BackgroundColor = Color.FromArgb("#B3090B0D");
        var indicator = new ActivityIndicator { Color = Color.FromArgb("#C8FF3D"), WidthRequest = 32, HeightRequest = 32 };
        indicator.SetBinding(ActivityIndicator.IsRunningProperty, new Binding(nameof(IsVisible), source: this));
        var label = new Label
        {
            Text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th" ? "กำลังดำเนินการ…" : "Please wait…",
            FontFamily = "NotoSansThaiRegular", FontSize = 15,
            TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center
        };
        Content = new Grid
        {
            Children =
            {
                new Border
                {
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    Padding = 24, Stroke = Color.FromArgb("#2A3036"), BackgroundColor = Color.FromArgb("#151A1E"),
                    StrokeShape = new RoundRectangle { CornerRadius = 20 },
                    Content = new VerticalStackLayout { Spacing = 12, Children = { indicator, label } }
                }
            }
        };
    }
}
