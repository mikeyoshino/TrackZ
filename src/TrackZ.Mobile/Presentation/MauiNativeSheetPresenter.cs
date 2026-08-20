namespace TrackZ.Mobile.Presentation;

public sealed class MauiNativeSheetPresenter : INativeSheetPresenter
{
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
            await navigation.PushModalAsync(page, true);
            NativeSheetConfiguration.Configure(page, detent);
        }
        finally
        {
            page.HandlerChanged -= Configure;
        }
#else
        await navigation.PushModalAsync(page, true);
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
        await navigation.PopModalAsync(true);
    }

    private static INavigation ResolveNavigation() =>
        Shell.Current?.Navigation
        ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
        ?? throw new InvalidOperationException("A visible application window is required to present a sheet.");
}
