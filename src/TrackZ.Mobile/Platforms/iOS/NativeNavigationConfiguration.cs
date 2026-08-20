#if IOS
using UIKit;

namespace TrackZ.Mobile.Presentation;

internal static class NativeNavigationConfiguration
{
    private static bool _configured;

    public static void Configure()
    {
        if (_configured) return;
        _configured = true;

        var appearance = new UINavigationBarAppearance();
        appearance.ConfigureWithOpaqueBackground();
        appearance.BackgroundColor = UIColor.FromRGB(9, 11, 13);
        appearance.TitleTextAttributes = new UIStringAttributes
        {
            ForegroundColor = UIColor.FromRGB(245, 247, 248)
        };
        appearance.LargeTitleTextAttributes = new UIStringAttributes
        {
            ForegroundColor = UIColor.FromRGB(245, 247, 248)
        };

        UINavigationBar.Appearance.StandardAppearance = appearance;
        UINavigationBar.Appearance.ScrollEdgeAppearance = appearance;
        UINavigationBar.Appearance.CompactAppearance = appearance;
        UINavigationBar.Appearance.TintColor = UIColor.FromRGB(200, 255, 61);
        UINavigationBar.Appearance.PrefersLargeTitles = true;
    }
}
#endif
