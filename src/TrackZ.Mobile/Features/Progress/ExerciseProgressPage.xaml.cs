using TrackZ.Mobile.Features.Gamification;

namespace TrackZ.Mobile.Features.Progress;

public partial class ExerciseProgressPage : ContentPage
{
    private readonly ExerciseProgressViewModel _viewModel;
    public ExerciseProgressPage(ExerciseProgressViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }
}
