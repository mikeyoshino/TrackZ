using System.ComponentModel;
using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Presentation;
using static TrackZ.Mobile.Features.History.HistoryPresentation;
using static TrackZ.Mobile.Features.History.HistoryCalendarViews;

namespace TrackZ.Mobile.Features.History;

public partial class WorkoutHistoryPage : ContentPage
{
    private readonly WorkoutHistoryViewModel _viewModel;
    private readonly INativeSheetPresenter _sheets;
    private ContentPage? _sheet;
    private bool _presenting;
    private bool _openingWorkout;
    private bool _restoreDayFocus;
    private bool _wasParented;
    private bool _deactivated;

    public WorkoutHistoryPage(WorkoutHistoryViewModel viewModel, INativeSheetPresenter sheets)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _sheets = sheets;
        FilterButton.FontFamily = "NotoSansThaiRegular";
        FilterButton.FontAttributes = FontAttributes.None;
        FilterButton.FontSize = 13;
        _viewModel.Calendar.PropertyChanged += OnCalendarChanged;
        _viewModel.PropertyChanged += OnViewModelChanged;
        RenderCalendar();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_deactivated) await _viewModel.LoadAsync();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is not null)
        {
            _wasParented = true;
            return;
        }
        if (_wasParented) Deactivate();
    }

    public void Deactivate()
    {
        if (_deactivated) return;
        _deactivated = true;
        _viewModel.Calendar.PropertyChanged -= OnCalendarChanged;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        if (_sheet is { } sheet) _ = DismissDeactivatedSheetAsync(sheet);
        CalendarHost.Content = null;
        _viewModel.Deactivate();
        BindingContext = null;
    }

    private async void OnWorkoutSelected(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_deactivated || _openingWorkout || eventArgs.CurrentSelection.FirstOrDefault() is not HistoryWorkoutItem workout) return;
        if (sender is CollectionView collection) collection.SelectedItem = null;
        _openingWorkout = true;
        try { await Shell.Current.GoToAsync($"workout-history-detail?workoutId={workout.WorkoutId:D}"); }
        finally { _openingWorkout = false; }
    }

    private void OnCalendarChanged(object? sender, PropertyChangedEventArgs args) => UpdateCalendar();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WorkoutHistoryViewModel.IsBusy) or nameof(WorkoutHistoryViewModel.ErrorMessage))
            UpdateCalendar();
    }

    private void UpdateCalendar()
    {
        if (_deactivated) return;
        if (MainThread.IsMainThread) RenderCalendar();
        else MainThread.BeginInvokeOnMainThread(() => { if (!_deactivated) RenderCalendar(); });
    }

    private void RenderCalendar()
    {
        var state = _viewModel.Calendar;
        FilterButton.Text = T("ตัวกรอง", "Filters") + (state.HasFilter ? $" ({state.Filter.Count})" : "");
        var calendar = Calendar(state, () => _ = OpenMonthAsync(), date =>
        {
            _restoreDayFocus = true;
            state.SelectDate(date);
        }, out var selectedDay);
        var stack = Stack(calendar);
        stack.Margin = new Thickness(0, 0, 0, 12);
        if (state.HasFilter)
        {
            var filter = Label(string.Join(" · ", state.Filter.Order().Select(HistoryPresentation.Body)), 13, true);
            var clear = Button(T("ล้างค่า", "Clear"), () => state.ApplyFilter([]));
            clear.SetDynamicResource(Microsoft.Maui.Controls.Button.TextColorProperty, "TrackZPrimary");
            var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
            row.Add(filter); row.Add(clear, 1); stack.Add(row);
        }
        var selected = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        selected.Add(Label(state.SelectedDateTitle, 16));
        selected.Add(Label(T($"{state.SelectedWorkouts.Count} การฝึก", $"{state.SelectedWorkouts.Count} workouts"), 13, true), 1);
        stack.Add(selected);
        if (_viewModel.IsBusy)
            stack.Add(new ActivityIndicator { IsRunning = true, HeightRequest = 24 });
        else if (!string.IsNullOrWhiteSpace(_viewModel.ErrorMessage))
            stack.Add(Button(_viewModel.Text.Retry, () => _ = _viewModel.LoadAsync()));
        else if (state.SelectedWorkouts.Count == 0)
        {
            var text = state.HasFilter
                ? T("ไม่มีการฝึกที่ตรงกับตัวกรองในวันนี้", "No matching workouts on this date")
                : state.SelectedDate > state.Today
                    ? T("วันที่นี้ยังมาไม่ถึง", "This date is still ahead")
                    : T("ยังไม่มีการฝึกที่บันทึกไว้ในวันนี้", "No workouts recorded on this date");
            stack.Add(Card(Stack(Label(text, 14), Label(
                state.HasFilter ? T("ลองเลือกวันอื่น หรือล้างตัวกรอง", "Try another date or clear the filters")
                : T("เลือกวันที่มีจุดเพื่อดูรายละเอียดการฝึก", "Choose a date with a dot to see your workout"), 13, true))));
        }
        if (_restoreDayFocus && selectedDay is not null)
        {
            _restoreDayFocus = false;
            selectedDay.Loaded += (_, _) =>
            {
                if (!_deactivated) selectedDay.SetSemanticFocus();
            };
        }
        CalendarHost.Content = stack;
    }

    private async void OnFilterClicked(object? sender, EventArgs args) => await OpenSheetAsync(false);
    private Task OpenMonthAsync() => OpenSheetAsync(true);

    private async Task OpenSheetAsync(bool month)
    {
        if (_deactivated || _presenting || _sheet is not null) return;
        _presenting = true;
        ContentPage? page = null;
        async Task Complete(object? value)
        {
            if (page is null) return;
            await _sheets.DismissAsync(page);
            _sheet = null;
            if (_deactivated) return;
            if (value is DateOnly date) _viewModel.Calendar.ShowMonth(date.Year, date.Month);
            if (value is BodyPart[] parts) _viewModel.Calendar.ApplyFilter(parts);
        }
        page = month
            ? new HistoryMonthSheet(_viewModel.Calendar.Month, _viewModel.Calendar.Today, Complete)
            : new HistoryFilterSheet(_viewModel.Calendar.Filter, Complete);
        _sheet = page;
        try { await _sheets.ShowAsync(page, NativeSheetDetent.Form); }
        catch
        {
            _sheet = null;
            if (!_deactivated) await DisplayAlertAsync(T("เปิดหน้าต่างไม่สำเร็จ", "Could not open"),
                T("กรุณาลองอีกครั้ง", "Please try again"), T("ตกลง", "OK"));
        }
        finally { _presenting = false; }
    }

    private async Task DismissDeactivatedSheetAsync(ContentPage page)
    {
        try { await _sheets.DismissAsync(page); }
        catch { /* The application window may already have been removed on account reset. */ }
        finally { _sheet = null; }
    }
}
