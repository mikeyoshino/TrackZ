using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Features.Train;

public partial class TrainPage : ContentPage
{
    private readonly TrainTodayViewModel _viewModel;
    private CoachDashboardPresenter? _coachPresenter;

    public HomeCopy Copy { get; } = HomeCopy.Current;
    public bool HasLatestWorkout => _viewModel.RepeatWorkout is not null;
    public string LatestWorkoutTitle => Copy.FormatLatestWorkoutTitle(_viewModel.RepeatWorkout);
    public string LatestWorkoutMeta => Copy.FormatLatestWorkoutMeta(_viewModel.RepeatWorkout, DateTimeOffset.Now);

    public TrainPage(TrainTodayViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    internal void SetCoachContent(View? week, View? advice)
    {
        SetHostContent(CoachWeekHost, week);
        CoachWeekSection.IsVisible = week is not null;

        SetHostContent(CoachHomeHost, advice);
        CoachHomeHost.IsVisible = advice is not null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        if (Handler?.MauiContext?.Services is { } services)
        {
            _coachPresenter ??= new CoachDashboardPresenter(services.GetRequiredService<TrainingCoachSource>(),
                services.GetRequiredService<CoachJournal>(), services.GetRequiredService<IAccountSessionBoundary>(),
                services.GetRequiredService<IWeightUnitPreference>(), services.GetRequiredService<IClock>(), this);
            _coachPresenter.Activate(LoadCoachAsync, () => SetCoachContent(null, null));
            await LoadCoachAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _coachPresenter?.Dispose();
        base.OnDisappearing();
    }

    private async Task LoadCoachAsync()
    {
        if (_coachPresenter is null) return;
        SetCoachContent(new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#C8FF3D") }, null);
        try
        {
            var report = await _coachPresenter.LoadAsync();
            if (report is not null)
                SetCoachContent(_coachPresenter.Week(report, _viewModel.HasAuthoritativeProgress ? _viewModel.WeeklyGoal : null),
                    _coachPresenter.Advice(report));
        }
        catch (OperationCanceledException) { SetCoachContent(null, null); }
        catch (Exception)
        {
            SetCoachContent(null, CoachUi.Stack(CoachUi.Label(CoachCopy.T("ยังโหลดคำแนะนำไม่ได้ เริ่มฝึกได้ตามปกติ", "Advice is unavailable. You can still start training."), 14, true),
                CoachUi.Button(CoachCopy.T("ลองอีกครั้ง", "Try again"), LoadCoachAsync)));
        }
    }

    private static void SetHostContent(VerticalStackLayout host, View? content)
    {
        host.Children.Clear();
        if (content is not null)
            host.Children.Add(content);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is not null and not nameof(TrainTodayViewModel.RepeatWorkout))
            return;

        OnPropertyChanged(nameof(LatestWorkoutTitle));
        OnPropertyChanged(nameof(LatestWorkoutMeta));
        OnPropertyChanged(nameof(HasLatestWorkout));
    }

    private async void OnViewSummaryClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//progress");

    private async void OnViewHistoryClicked(object? sender, EventArgs eventArgs) =>
        await Shell.Current.GoToAsync("//history");
}
