#if IOS
using UIKit;

namespace TrackZ.Mobile.Presentation;

internal static class NativeSheetConfiguration
{
    public static void Configure(UIViewController controller, NativeSheetDetent detent)
    {
        controller.ModalPresentationStyle = UIModalPresentationStyle.PageSheet;
        controller.ModalInPresentation = true;
        if (controller.SheetPresentationController is not { } sheet) return;

        sheet.Detents = detent switch
        {
            NativeSheetDetent.Medium => [UISheetPresentationControllerDetent.CreateMediumDetent()],
            NativeSheetDetent.Form when OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16)
                => [UISheetPresentationControllerDetent.Create("trackz-form",
                context => OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16)
                    ? (System.Runtime.InteropServices.NFloat)Math.Min(560, (double)context.MaximumDetentValue)
                    : 560)],
            _ => [UISheetPresentationControllerDetent.CreateLargeDetent()]
        };
        sheet.PrefersGrabberVisible = true;
        sheet.PreferredCornerRadius = 28;
    }

    public static UIViewController? FindTopViewController(UIViewController? controller)
    {
        while (controller?.PresentedViewController is { } presented)
            controller = presented;
        return controller;
    }
}
#endif
