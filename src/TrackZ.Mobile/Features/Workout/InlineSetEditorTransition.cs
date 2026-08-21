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

public sealed class MauiInlineSetEditorTransition : IInlineSetEditorTransition
{
    public async Task RevealAsync(
        ScrollView scroll,
        VisualElement editor,
        VisualElement focusTarget,
        string announcement,
        CancellationToken cancellationToken)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await scroll.ScrollToAsync(editor, ScrollToPosition.Center, animated: true);
            cancellationToken.ThrowIfCancellationRequested();
            focusTarget.Focus();
            cancellationToken.ThrowIfCancellationRequested();
            SemanticScreenReader.Default.Announce(announcement);
        });
    }

    public void RestoreFocus(VisualElement target) =>
        MainThread.BeginInvokeOnMainThread(() => target.Focus());
}
