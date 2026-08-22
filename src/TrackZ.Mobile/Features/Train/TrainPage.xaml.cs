namespace TrackZ.Mobile.Features.Train;

public partial class TrainPage : ContentPage
{
    private readonly TrainTodayViewModel _viewModel;

    public TrainPage(TrainTodayViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
