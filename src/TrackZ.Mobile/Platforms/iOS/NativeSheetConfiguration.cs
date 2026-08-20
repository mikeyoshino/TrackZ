#if IOS
using UIKit;

namespace TrackZ.Mobile.Presentation;

internal static class NativeSheetConfiguration
{
    public static void Configure(ContentPage page, NativeSheetDetent detent)
    {
        if (page.Handler?.PlatformView is not UIView view) return;
        var controller = FindViewController(view);
        if (controller?.SheetPresentationController is not { } sheet) return;

        sheet.Detents = detent == NativeSheetDetent.Medium
            ? [UISheetPresentationControllerDetent.CreateMediumDetent()]
            : [UISheetPresentationControllerDetent.CreateLargeDetent()];
        sheet.PrefersGrabberVisible = true;
        sheet.PreferredCornerRadius = 28;
    }

    private static UIViewController? FindViewController(UIResponder responder)
    {
        UIResponder? current = responder;
        while (current is not null)
        {
            if (current is UIViewController controller) return controller;
            current = current.NextResponder;
        }
        return null;
    }
}
#endif
