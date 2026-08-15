namespace TrackZ.Mobile.Features.History;

public sealed class MauiHistoryConfirmation : IHistoryConfirmation
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = Application.Current?.Windows.FirstOrDefault()?.Page
            ?? throw new InvalidOperationException("No active window is available for confirmation.");
        var confirmed = await page.DisplayAlertAsync(title, message, accept, cancel);
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }
}
