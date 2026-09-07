using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Train;

public partial class BodyAreaSheetPage : ContentPage
{
    private Func<BodyPart?, Task>? _completion;

    public BodyAreaSheetPage(WorkoutTextSet text)
    {
        InitializeComponent();
        Text = text;
        BindingContext = this;
    }

    public WorkoutTextSet Text { get; }
    public bool AllSelected { get; private set; }
    private bool _isAdding;
    public bool IsAdding
    {
        get => _isAdding;
        set { _isAdding = value; OnPropertyChanged(); OnPropertyChanged(nameof(SheetTitle)); }
    }
    public string SheetTitle => IsAdding ? Text.AddExercise : Text.ChooseWorkout;
    private async void OnAllClicked(object? sender, EventArgs args)
    {
        AllSelected = true;
        await CompleteAsync(null);
    }

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

    private async void OnCloseClicked(object? sender, EventArgs eventArgs) =>
        await CancelAsync();
}
