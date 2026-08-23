using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Presentation;

public sealed class MauiNativeSheetPresenter(
    IReduceMotionPreference reduceMotion) : INativeSheetPresenter
{
    internal bool AnimationsEnabled => !reduceMotion.IsEnabled;

    public async Task ShowAsync(
        ContentPage page,
        NativeSheetDetent detent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();
        var navigation = ResolveNavigation();

#if IOS
        void Configure(object? sender, EventArgs args) =>
            NativeSheetConfiguration.Configure(page, detent);
        page.HandlerChanged += Configure;
        try
        {
            await navigation.PushModalAsync(page, AnimationsEnabled);
            NativeSheetConfiguration.Configure(page, detent);
        }
        finally
        {
            page.HandlerChanged -= Configure;
        }
#else
        await navigation.PushModalAsync(page, AnimationsEnabled);
#endif
    }

    public async Task DismissAsync(
        ContentPage page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();
        var navigation = ResolveNavigation();
        if (!ReferenceEquals(navigation.ModalStack.LastOrDefault(), page)) return;
        await navigation.PopModalAsync(AnimationsEnabled);
    }

    private static INavigation ResolveNavigation() =>
        Shell.Current?.Navigation
        ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
        ?? throw new InvalidOperationException("A visible application window is required to present a sheet.");
}
