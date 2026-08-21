namespace TrackZ.Mobile.Features.Workout;

public interface IInlineSetEditorTransition
{
    Task RevealAsync(
        ScrollView scroll,
        VisualElement editor,
        VisualElement focusTarget,
        string announcement,
        CancellationToken cancellationToken);

    void RestoreFocus(VisualElement target);
}

public sealed class MauiInlineSetEditorTransition(
    Func<Func<Task>, Task>? invokeOnMainThread = null,
    Func<ScrollView, double, double, bool, Task>? scrollTo = null) : IInlineSetEditorTransition
{
    private readonly Func<Func<Task>, Task> _invokeOnMainThread = invokeOnMainThread
        ?? (action => MainThread.InvokeOnMainThreadAsync(action));
    private readonly Func<ScrollView, double, double, bool, Task> _scrollTo = scrollTo
        ?? ((viewport, x, y, animated) => viewport.ScrollToAsync(x, y, animated));

    public async Task RevealAsync(
        ScrollView scroll,
        VisualElement editor,
        VisualElement focusTarget,
        string announcement,
        CancellationToken cancellationToken)
    {
        await _invokeOnMainThread(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var centered = scroll.GetScrollPositionForElement(editor, ScrollToPosition.Center);
            await _scrollTo(scroll, 0, centered.Y, true);
            cancellationToken.ThrowIfCancellationRequested();
            focusTarget.Focus();
            cancellationToken.ThrowIfCancellationRequested();
            SemanticScreenReader.Default.Announce(announcement);
        });
    }

    public void RestoreFocus(VisualElement target) =>
        MainThread.BeginInvokeOnMainThread(() => target.Focus());
}
