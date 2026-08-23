using System.Globalization;
using TrackZ.Mobile.Features.Gamification;

namespace TrackZ.Mobile.Features.Progress;

public partial class ExerciseProgressPage : ContentPage, IQueryAttributable
{
    private readonly ProgressDashboardViewModel _viewModel;
    private Guid? _requestedExerciseId;

    public ExerciseProgressPage(ProgressDashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _requestedExerciseId = query.TryGetValue("exerciseId", out var raw)
            && Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var parsed)
                ? parsed
                : null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        var item = ConsumeRequestedExercise();
        if (item is null) return;

        var index = _viewModel.Exercises.IndexOf(item);
        if (index >= 0 && index < ExerciseProgressRows.Children.Count)
        {
            var row = ExerciseProgressRows.Children[index];
            await ExerciseProgressScroll.ScrollToAsync(0, row.Frame.Y, animated: false);
        }
    }

    internal ExerciseProgressItem? ConsumeRequestedExercise()
    {
        var requested = _requestedExerciseId;
        _requestedExerciseId = null;
        return requested is { } id
            ? _viewModel.Exercises.SingleOrDefault(candidate => candidate.ExerciseId == id)
            : null;
    }
}
