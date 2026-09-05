namespace TrackZ.Mobile.Presentation;

public enum NativeSheetDetent
{
    Medium = 1,
    Large = 2,
    Form = 3
}

public interface INativeSheetPresenter
{
    Task ShowAsync(
        ContentPage page,
        NativeSheetDetent detent,
        CancellationToken cancellationToken = default);

    Task DismissAsync(
        ContentPage page,
        CancellationToken cancellationToken = default);
}
