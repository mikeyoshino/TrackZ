using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Train;

public sealed class MauiBodyAreaPicker(
    INativeSheetPresenter presenter,
    Func<BodyAreaSheetPage> createPage) : IBodyAreaPicker
{
    public async Task<BodyPart?> PickAsync(CancellationToken cancellationToken = default)
        => (await PickCoreAsync(false, cancellationToken)).BodyPart;

    public Task<(bool Confirmed, BodyPart? BodyPart)> PickForAddingAsync(CancellationToken cancellationToken = default)
        => PickCoreAsync(true, cancellationToken);

    private async Task<(bool Confirmed, BodyPart? BodyPart)> PickCoreAsync(bool adding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = createPage();
        page.IsAdding = adding;
        var result = new TaskCompletionSource<BodyPart?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        page.SetCompletion(async bodyPart =>
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            await presenter.DismissAsync(page, CancellationToken.None);
            result.TrySetResult(bodyPart);
        });

        using var cancellation = cancellationToken.Register(() => _ = page.CancelAsync());
        await presenter.ShowAsync(page, NativeSheetDetent.Medium, cancellationToken);
        var selected = await result.Task;
        return (selected.HasValue || page.AllSelected, selected);
    }
}
