namespace TrackZ.Mobile.Features.Exercises;

public sealed record ExerciseOptionSheetItem(string Label, object Value, bool IsSelected);

public partial class ExerciseOptionSheetPage : ContentPage
{
    private Func<object?, Task>? _completion;

    public ExerciseOptionSheetPage(string title, IReadOnlyList<ExerciseOptionSheetItem> options)
    {
        InitializeComponent();
        TitleText = title;
        Options = options;
        CancelText = options.Count > 0 ? title : string.Empty;
        BindingContext = this;
    }

    public string TitleText { get; }
    public string CancelText { get; }
    public IReadOnlyList<ExerciseOptionSheetItem> Options { get; }

    internal void SetCompletion(Func<object?, Task> completion) =>
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));

    public Task SelectAsync(object value) => CompleteAsync(value);

    public Task CancelAsync() => CompleteAsync(null);

    private Task CompleteAsync(object? value) =>
        _completion?.Invoke(value) ?? Task.CompletedTask;

    private async void OnOptionClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: ExerciseOptionSheetItem option })
            await SelectAsync(option.Value);
    }

    private async void OnCloseClicked(object? sender, EventArgs eventArgs) =>
        await CancelAsync();
}
