namespace TrackZ.Mobile.Features.Exercises;

public interface IExercisePickerWarning
{
    Task ShowAsync(
        Page page,
        string title,
        string message,
        string dismiss,
        CancellationToken cancellationToken = default);
}

public sealed class MauiExercisePickerWarning : IExercisePickerWarning
{
    public async Task ShowAsync(
        Page page,
        string title,
        string message,
        string dismiss,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        cancellationToken.ThrowIfCancellationRequested();
        await page.DisplayAlertAsync(title, message, dismiss);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
