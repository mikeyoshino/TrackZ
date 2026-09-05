using TrackZ.Mobile.Features.Workout;
#if IOS
using Microsoft.Maui.Platform;
using UIKit;
#endif

namespace TrackZ.Mobile.Presentation;

public sealed class MauiNativeSheetPresenter(
    IReduceMotionPreference reduceMotion) : INativeSheetPresenter
{
#if IOS
    private readonly Dictionary<ContentPage, UIViewController> _presentedSheets = [];
#endif

    internal bool AnimationsEnabled => !reduceMotion.IsEnabled;

    public async Task ShowAsync(
        ContentPage page,
        NativeSheetDetent detent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();

#if IOS
        var windowHandler = Application.Current?.Windows.FirstOrDefault()?.Handler;
        var mauiContext = windowHandler?.MauiContext
            ?? throw new InvalidOperationException("A visible MAUI window is required to present a sheet.");
        var platformWindow = windowHandler.PlatformView as UIWindow
            ?? throw new InvalidOperationException("A visible iOS window is required to present a sheet.");
        var presenter = NativeSheetConfiguration.FindTopViewController(platformWindow.RootViewController)
            ?? throw new InvalidOperationException("A visible iOS view controller is required to present a sheet.");
        var controller = page.ToUIViewController(mauiContext);
        NativeSheetConfiguration.Configure(controller, detent);
        _presentedSheets.Add(page, controller);
        try
        {
            await presenter.PresentViewControllerAsync(controller, AnimationsEnabled);
        }
        catch
        {
            _presentedSheets.Remove(page);
            throw;
        }
#else
        var navigation = ResolveNavigation();
        await navigation.PushModalAsync(page, AnimationsEnabled);
#endif
    }

    public async Task DismissAsync(
        ContentPage page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();
#if IOS
        if (!_presentedSheets.Remove(page, out var controller)) return;
        await controller.DismissViewControllerAsync(AnimationsEnabled);
#else
        var navigation = ResolveNavigation();
        if (!ReferenceEquals(navigation.ModalStack.LastOrDefault(), page)) return;
        await navigation.PopModalAsync(AnimationsEnabled);
#endif
    }

    private static INavigation ResolveNavigation() =>
        Shell.Current?.Navigation
        ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
        ?? throw new InvalidOperationException("A visible application window is required to present a sheet.");
}
