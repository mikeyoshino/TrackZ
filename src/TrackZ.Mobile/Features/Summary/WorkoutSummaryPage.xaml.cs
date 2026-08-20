using TrackZ.Mobile.Features.Gamification;

namespace TrackZ.Mobile.Features.Summary;

public partial class WorkoutSummaryPage : ContentPage, IQueryAttributable
{
    private readonly WorkoutSummaryViewModel _viewModel;
    public WorkoutSummaryPage(WorkoutSummaryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("workoutId", out var value) && Guid.TryParse(value?.ToString(), out var workoutId))
            await _viewModel.LoadAsync(workoutId);
    }
}
