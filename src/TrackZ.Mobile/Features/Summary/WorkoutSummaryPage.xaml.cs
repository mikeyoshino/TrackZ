using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.Summary;

public partial class WorkoutSummaryPage : ContentPage, IQueryAttributable
{
    private readonly WorkoutSummaryViewModel _viewModel;
    private readonly ITrackZMotion _motion;
    private CancellationTokenSource? _lifetime;
    public WorkoutSummaryPage(WorkoutSummaryViewModel viewModel, ITrackZMotion motion)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _motion = motion;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("workoutId", out var value) && Guid.TryParse(value?.ToString(), out var workoutId))
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
            try
            {
                await _viewModel.LoadAsync(workoutId, _lifetime.Token);
                await _motion.PlayWorkoutSummaryAsync(SummaryReveal, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch { SummaryReveal.Opacity = 1; }
        }
    }

    protected override void OnDisappearing()
    {
        _lifetime?.Cancel();
        _motion.Cancel(SummaryReveal);
        base.OnDisappearing();
    }
}
