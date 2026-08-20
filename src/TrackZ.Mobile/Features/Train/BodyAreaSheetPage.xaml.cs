using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Train;

public partial class BodyAreaSheetPage : ContentPage
{
    private Func<BodyPart?, Task>? _completion;

    public BodyAreaSheetPage()
    {
        InitializeComponent();
        Text = WorkoutResources.Current;
        BindingContext = this;
    }

    public WorkoutTextSet Text { get; }

    internal void SetCompletion(Func<BodyPart?, Task> completion) =>
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));

    public Task SelectAsync(BodyPart bodyPart) => CompleteAsync(bodyPart);

    public Task CancelAsync() => CompleteAsync(null);

    private Task CompleteAsync(BodyPart? bodyPart) =>
        _completion?.Invoke(bodyPart) ?? Task.CompletedTask;

    private async void OnBodyAreaClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: string value }
            && Enum.TryParse<BodyPart>(value, out var bodyPart))
            await SelectAsync(bodyPart);
    }

    private async void OnCancelClicked(object? sender, EventArgs eventArgs) =>
        await CancelAsync();
}
