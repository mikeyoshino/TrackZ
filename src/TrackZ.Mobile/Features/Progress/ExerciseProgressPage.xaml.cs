using System.Globalization;
using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Features.Coach;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Features.Exercises;
using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Mobile.Features.Progress;

public partial class ExerciseProgressPage : ContentPage, IQueryAttributable
{
    private readonly ProgressDashboardViewModel _viewModel;
    private readonly IAccountSessionBoundary _boundary;
    private readonly Func<ScrollView, Element, ScrollToPosition, bool, Task> _scrollTo;
    private Guid? _requestedExerciseId;
    private CancellationTokenSource? _focusCancellation;
    private CoachDashboardPresenter? _coachPresenter;

    public ExerciseProgressPage(
        ProgressDashboardViewModel viewModel,
        IAccountSessionBoundary boundary)
        : this(
            viewModel,
            boundary,
            static (scroll, element, position, animated) =>
                scroll.ScrollToAsync(element, position, animated))
    {
    }

    internal ExerciseProgressPage(
        ProgressDashboardViewModel viewModel,
        IAccountSessionBoundary boundary,
        Func<ScrollView, Element, ScrollToPosition, bool, Task> scrollTo)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _boundary = boundary;
        _scrollTo = scrollTo;
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
        await HandleAppearingAsync();
    }

    internal async Task HandleAppearingAsync()
    {
        _focusCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _focusCancellation = cancellation;
        var generation = _boundary.Capture();
        using var lease = _boundary.CreateCancellationLease(generation, cancellation.Token);
        var cancellationToken = lease.Token;
        try
        {
            await _viewModel.LoadAsync(cancellationToken);
            if (Handler?.MauiContext?.Services is { } services)
            {
                _coachPresenter ??= new CoachDashboardPresenter(services.GetRequiredService<TrainingCoachSource>(),
                    services.GetRequiredService<CoachJournal>(), _boundary, services.GetRequiredService<IWeightUnitPreference>(),
                    services.GetRequiredService<IClock>(), this);
                _coachPresenter.Activate(LoadCoachAsync, () => CoachReportHost.Children.Clear());
                await LoadCoachAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();
            _ = await _boundary.TryCommitAsync(
                generation,
                FocusRequestedExerciseAsync,
                cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellation.IsCancellationRequested
            || _boundary.IsCancellationRequested(generation))
        {
        }
        finally
        {
            if (ReferenceEquals(_focusCancellation, cancellation))
                _focusCancellation = null;
            cancellation.Dispose();
        }
    }

    protected override void OnDisappearing()
    {
        _coachPresenter?.Dispose();
        _focusCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadCoachAsync()
    {
        if (_coachPresenter is null) return;
        CoachReportHost.Children.Clear();
        var loading = new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#C8FF3D") };
        CoachReportHost.Children.Add(loading);
        try
        {
            var report = await _coachPresenter.LoadAsync();
            CoachReportHost.Children.Clear();
            if (report is not null) CoachReportHost.Children.Add(_coachPresenter.Report(report,
                _viewModel.HasAuthoritativeProgressData ? _viewModel.WeeklyGoal : null));
        }
        catch (OperationCanceledException) { CoachReportHost.Children.Clear(); }
        catch (Exception)
        {
            CoachReportHost.Children.Clear();
            CoachReportHost.Children.Add(CoachUi.Label(CoachCopy.T("ยังโหลดรายงานไม่ได้", "Could not load your report"), 15, true));
            CoachReportHost.Children.Add(CoachUi.Button(CoachCopy.T("ลองอีกครั้ง", "Try again"), LoadCoachAsync));
        }
    }

    internal async Task FocusRequestedExerciseAsync(CancellationToken cancellationToken = default)
    {
        var item = ConsumeRequestedExercise();
        if (item is null) return;

        var index = _viewModel.Exercises.IndexOf(item);
        if (index < 0 || index >= ExerciseProgressRows.Children.Count) return;

        var row = ExerciseProgressRows.Children[index] as VisualElement;
        if (row is null) return;
        await WaitForPositiveLayoutAsync(row, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _scrollTo(ExerciseProgressScroll, row, ScrollToPosition.Start, false);
    }

    internal ExerciseProgressItem? ConsumeRequestedExercise()
    {
        var requested = _requestedExerciseId;
        _requestedExerciseId = null;
        return requested is { } id
            ? _viewModel.Exercises.SingleOrDefault(candidate => candidate.ExerciseId == id)
            : null;
    }

    private static async Task WaitForPositiveLayoutAsync(
        VisualElement row,
        CancellationToken cancellationToken)
    {
        if (HasPositiveLayout(row)) return;

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? sizeChanged = null;
        sizeChanged = (_, _) =>
        {
            if (HasPositiveLayout(row)) completion.TrySetResult();
        };
        row.SizeChanged += sizeChanged;
        using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            if (HasPositiveLayout(row)) completion.TrySetResult();
            await completion.Task;
        }
        finally
        {
            row.SizeChanged -= sizeChanged;
        }
    }

    private static bool HasPositiveLayout(VisualElement row) =>
        row.Width > 0 && row.Height > 0;
}
