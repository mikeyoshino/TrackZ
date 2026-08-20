namespace TrackZ.Mobile.Features.History;

public partial class WorkoutHistoryPage : ContentPage
{
    private readonly WorkoutHistoryViewModel _viewModel;
    private bool _wasParented;
    private bool _deactivated;

    public WorkoutHistoryPage(WorkoutHistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_deactivated) await _viewModel.LoadAsync();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is not null)
        {
            _wasParented = true;
            return;
        }
        if (_wasParented) Deactivate();
    }

    public void Deactivate()
    {
        if (_deactivated) return;
        _deactivated = true;
        _viewModel.Deactivate();
        BindingContext = null;
    }

    private async void OnWorkoutSelected(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (eventArgs.CurrentSelection.FirstOrDefault() is not HistoryWorkoutItem workout) return;
        if (sender is CollectionView collection) collection.SelectedItem = null;
        await Shell.Current.GoToAsync($"workout-history-detail?workoutId={workout.WorkoutId:D}");
    }
}
