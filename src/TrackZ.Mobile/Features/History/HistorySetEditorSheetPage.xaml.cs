using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.History;

public partial class HistorySetEditorSheetPage : ContentPage
{
    private readonly INativeSheetPresenter _presenter;
    private WorkoutHistoryDetailViewModel? _detail;
    private HistorySetItem? _set;
    private decimal? _originalWeight;
    private decimal? _originalAssistance;
    private int _originalReps;
    private bool _committed;

    public HistorySetEditorSheetPage(INativeSheetPresenter presenter)
    {
        _presenter = presenter;
        Text = TrackZ.Mobile.Features.Workout.WorkoutResources.Current;
        InitializeComponent();
    }

    public TrackZ.Mobile.Features.Workout.WorkoutTextSet Text { get; }

    public async Task PresentAsync(WorkoutHistoryDetailViewModel detail, HistorySetItem set)
    {
        _detail = detail;
        _set = set;
        _originalWeight = set.WeightKg;
        _originalAssistance = set.AssistedKg;
        _originalReps = set.Reps;
        _committed = false;
        BindingContext = set;
        await _presenter.ShowAsync(this, NativeSheetDetent.Medium);
    }

    private async void OnSaveClicked(object? sender, EventArgs eventArgs)
    {
        if (_detail is not null && _set is not null)
            await _detail.EditSetCommand.ExecuteAsync(_set);
        _committed = true;
        await DismissAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs eventArgs)
    {
        if (_detail is not null && _set is not null)
            await _detail.DeleteSetCommand.ExecuteAsync(_set);
        _committed = true;
        await DismissAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs eventArgs) => await DismissAsync();

    private async Task DismissAsync()
    {
        RestoreUncommittedEdit();
        await _presenter.DismissAsync(this);
        BindingContext = null;
        _detail = null;
        _set = null;
    }

    protected override void OnDisappearing()
    {
        RestoreUncommittedEdit();
        base.OnDisappearing();
    }

    private void RestoreUncommittedEdit()
    {
        if (_committed || _set is null) return;
        _set.WeightKg = _originalWeight;
        _set.AssistedKg = _originalAssistance;
        _set.Reps = _originalReps;
    }
}
