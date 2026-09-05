using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Exercises;

public interface IExerciseOptionPicker
{
    Task<LocalizedBodyPartOption?> PickBodyPartAsync(
        string title,
        IReadOnlyList<LocalizedBodyPartOption> options,
        LocalizedBodyPartOption? selected,
        CancellationToken cancellationToken = default);

    Task<LocalizedTrackingModeOption?> PickTrackingModeAsync(
        string title,
        IReadOnlyList<LocalizedTrackingModeOption> options,
        LocalizedTrackingModeOption? selected,
        CancellationToken cancellationToken = default);
}

public sealed class MauiExerciseOptionPicker(INativeSheetPresenter presenter) : IExerciseOptionPicker
{
    public Task<LocalizedBodyPartOption?> PickBodyPartAsync(
        string title,
        IReadOnlyList<LocalizedBodyPartOption> options,
        LocalizedBodyPartOption? selected,
        CancellationToken cancellationToken = default) =>
        PickAsync(title, options, selected, option => option.Label, cancellationToken);

    public Task<LocalizedTrackingModeOption?> PickTrackingModeAsync(
        string title,
        IReadOnlyList<LocalizedTrackingModeOption> options,
        LocalizedTrackingModeOption? selected,
        CancellationToken cancellationToken = default) =>
        PickAsync(title, options, selected, option => option.Label, cancellationToken);

    private async Task<TOption?> PickAsync<TOption>(
        string title,
        IReadOnlyList<TOption> options,
        TOption? selected,
        Func<TOption, string> getLabel,
        CancellationToken cancellationToken)
        where TOption : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = new ExerciseOptionSheetPage(
            title,
            options.Select(option => new ExerciseOptionSheetItem(
                getLabel(option),
                option,
                EqualityComparer<TOption>.Default.Equals(option, selected))).ToArray());
        var result = new TaskCompletionSource<TOption?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        page.SetCompletion(async value =>
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            await presenter.DismissAsync(page, CancellationToken.None);
            result.TrySetResult(value as TOption);
        });

        using var cancellation = cancellationToken.Register(() => _ = page.CancelAsync());
        var detent = options.Count > 4
            ? NativeSheetDetent.Large
            : NativeSheetDetent.Medium;
        await presenter.ShowAsync(page, detent, cancellationToken);
        return await result.Task;
    }
}
