namespace TrackZ.Mobile.Features.Workout;

public interface IInlineSetEditorTransition
{
    Task RevealAsync(
        ScrollView scroll,
        VisualElement editor,
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
        string announcement,
        CancellationToken cancellationToken)
    {
        await _invokeOnMainThread(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WaitForPositiveLayoutAsync(editor, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // The editor is the first content child. Resetting the vertical viewport does
            // not depend on its retained frame and keeps both controls visible without
            // opening the numeric keyboard for one field before the user chooses it.
            await _scrollTo(scroll, 0, 0, true);
            cancellationToken.ThrowIfCancellationRequested();
            SemanticScreenReader.Default.Announce(announcement);
        });
    }

    public void RestoreFocus(VisualElement target) =>
        MainThread.BeginInvokeOnMainThread(() => target.Focus());

    private static async Task WaitForPositiveLayoutAsync(
        VisualElement editor,
        CancellationToken cancellationToken)
    {
        if (HasPositiveLayout(editor)) return;
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? sizeChanged = null;
        sizeChanged = (_, _) =>
        {
            if (HasPositiveLayout(editor)) completion.TrySetResult();
        };
        editor.SizeChanged += sizeChanged;
        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            if (HasPositiveLayout(editor)) completion.TrySetResult();
            await completion.Task;
        }
        finally
        {
            editor.SizeChanged -= sizeChanged;
        }
    }

    private static bool HasPositiveLayout(VisualElement editor) =>
        editor.Width > 0 && editor.Height > 0;
}
