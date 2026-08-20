namespace TrackZ.Mobile.Features.History;

public partial class WorkoutHistoryDetailPage : ContentPage, IQueryAttributable
{
    private readonly WorkoutHistoryDetailViewModel _viewModel;
    private readonly HistorySetEditorSheetPage _editor;
    private readonly HistoryConflictSheetPage _conflicts;

    public WorkoutHistoryDetailPage(
        WorkoutHistoryDetailViewModel viewModel,
        HistorySetEditorSheetPage editor,
        HistoryConflictSheetPage conflicts)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _editor = editor;
        _conflicts = conflicts;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("workoutId", out var raw)
            && Guid.TryParse(raw?.ToString(), out var workoutId)
            && workoutId != Guid.Empty)
            await _viewModel.LoadAsync(workoutId);
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is null && BindingContext is not null)
        {
            _viewModel.Deactivate();
            BindingContext = null;
        }
    }

    private async void OnEditSetClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is Button { CommandParameter: HistorySetItem set })
            await _editor.PresentAsync(_viewModel, set);
    }

    private async void OnConflictClicked(object? sender, EventArgs eventArgs)
    {
        if (_viewModel.Workout is { } workout)
            await _conflicts.PresentAsync(_viewModel, workout);
    }
}
