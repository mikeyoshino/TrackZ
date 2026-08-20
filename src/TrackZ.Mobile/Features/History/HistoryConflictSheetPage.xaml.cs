using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Features.History;

public partial class HistoryConflictSheetPage : ContentPage
{
    private readonly INativeSheetPresenter _presenter;
    private WorkoutHistoryDetailViewModel? _detail;
    private HistoryWorkoutItem? _workout;

    public HistoryConflictSheetPage(INativeSheetPresenter presenter)
    {
        _presenter = presenter;
        InitializeComponent();
    }

    public async Task PresentAsync(WorkoutHistoryDetailViewModel detail, HistoryWorkoutItem workout)
    {
        _detail = detail;
        _workout = workout;
        BindingContext = detail;
        await _presenter.ShowAsync(this, NativeSheetDetent.Medium);
    }

    private async void OnKeepServerClicked(object? sender, EventArgs eventArgs)
    {
        if (_detail is not null && _workout is not null
            && WorkoutHistoryDetailViewModel.CanResolveDestructively(_workout))
            await _detail.KeepServerCommand.ExecuteAsync(_workout);
        await DismissAsync();
    }

    private async void OnApplyLocalClicked(object? sender, EventArgs eventArgs)
    {
        if (_detail is not null && _workout is not null
            && WorkoutHistoryDetailViewModel.CanResolveDestructively(_workout))
            await _detail.ApplyLocalCommand.ExecuteAsync(_workout);
        await DismissAsync();
    }

    private async Task DismissAsync()
    {
        await _presenter.DismissAsync(this);
        BindingContext = null;
        _detail = null;
        _workout = null;
    }
}
