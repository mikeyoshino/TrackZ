using System.Globalization;
using TrackZ.Mobile.Features.Gamification;

namespace TrackZ.Mobile.Features.Progress;

public partial class ExerciseProgressPage : ContentPage, IQueryAttributable
{
    private readonly ProgressDashboardViewModel _viewModel;
    private readonly Func<ScrollView, double, double, bool, Task> _scrollTo;
    private Guid? _requestedExerciseId;
    private CancellationTokenSource? _focusCancellation;

    public ExerciseProgressPage(ProgressDashboardViewModel viewModel)
        : this(viewModel, static (scroll, x, y, animated) => scroll.ScrollToAsync(x, y, animated))
    {
    }

    internal ExerciseProgressPage(
        ProgressDashboardViewModel viewModel,
        Func<ScrollView, double, double, bool, Task> scrollTo)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
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
        var cancellationToken = cancellation.Token;
        try
        {
            await _viewModel.LoadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await FocusRequestedExerciseAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        _focusCancellation?.Cancel();
        base.OnDisappearing();
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
        await _scrollTo(ExerciseProgressScroll, 0, row.Frame.Y, false);
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
