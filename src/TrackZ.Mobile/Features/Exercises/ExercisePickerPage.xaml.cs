using System.Windows.Input;
using TrackZ.Mobile.Features.Exercises.Models;
using TrackZ.Mobile.Features.Workout;
#if IOS
using UIKit;
#endif

namespace TrackZ.Mobile.Features.Exercises;

public partial class ExercisePickerPage : ContentPage, IQueryAttributable
{
    private readonly ExercisePickerViewModel _viewModel;
    private readonly IExercisePickerNavigator _navigator;
    private readonly IExercisePickerWarning _warning;
    private readonly IReduceMotionPreference _reduceMotion;
    private TrackZ.Domain.Exercises.BodyPart? _requestedBodyPart;
    private Grid? _filterOverlay;
    private BoxView? _filterScrim;
    private Border? _filterSheet;
    private bool _filterClosing;
    private bool _loaded;
    private bool _hasActivePickerRequest;

    public ExercisePickerPage(
        ExercisePickerViewModel viewModel,
        IExercisePickerNavigator navigator,
        IExercisePickerWarning warning,
        IReduceMotionPreference reduceMotion)
    {
        _viewModel = viewModel;
        _navigator = navigator;
        _warning = warning;
        _reduceMotion = reduceMotion;
        EditCustomCommand = new Command<CachedExercise>(EditCustomExercise);
        OpenTechniqueCommand = new AsyncCommand(OpenTechniqueAsync);
        DoneCommand = new AsyncCommand(_ => CompleteSelectionAsync());
        InitializeComponent();
        BindingContext = _viewModel;
    }

    public event Func<IReadOnlyCollection<Guid>, Task>? SelectionCompleted;
    public ICommand EditCustomCommand { get; }
    public IAsyncCommand OpenTechniqueCommand { get; }
    public IAsyncCommand DoneCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("bodyPart", out var raw)) return;
        var value = Uri.UnescapeDataString(Convert.ToString(raw) ?? string.Empty);
        TrackZ.Domain.Exercises.BodyPart? requestedBodyPart;
        if (value == "all") requestedBodyPart = null;
        else if (int.TryParse(value, out var number)
            && Enum.IsDefined(typeof(TrackZ.Domain.Exercises.BodyPart), number))
            requestedBodyPart = (TrackZ.Domain.Exercises.BodyPart)number;
        else return;

        var startsNewDraft = !_hasActivePickerRequest || requestedBodyPart != _requestedBodyPart;
        if (startsNewDraft)
        {
            _loaded = false;
            _viewModel.ResetSelection();
            _viewModel.ApplyFilters([], []);
        }
        _hasActivePickerRequest = true;
        _requestedBodyPart = requestedBodyPart;
        _viewModel.SelectedBodyPart = requestedBodyPart;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var bodies = _viewModel.SelectedBodyParts.ToArray();
        var equipment = _viewModel.SelectedEquipment.ToArray();
        await _viewModel.LoadAsync(_requestedBodyPart);
        if (_loaded) _viewModel.ApplyFilters(bodies, equipment);
        _loaded = true;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        EndPickerDraft();
        await _navigator.ReturnToWorkoutAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_filterOverlay is null) return base.OnBackButtonPressed();
        _ = CloseFiltersAsync();
        return true;
    }

    private async Task CloseFiltersAsync()
    {
        if (_filterOverlay is null || _filterClosing) return;
        _filterClosing = true;
        var overlay = _filterOverlay;
        var scrim = _filterScrim;
        var sheet = _filterSheet;
        try
        {
            scrim?.CancelAnimations();
            sheet?.CancelAnimations();
            if (!_reduceMotion.IsEnabled && scrim is not null && sheet is not null && Handler is not null)
            {
                await Task.WhenAll(
                    scrim.FadeToAsync(0, 160, Easing.CubicIn),
                    sheet.TranslateToAsync(0, Math.Max(sheet.Height, 420), 180, Easing.CubicIn));
            }
        }
        finally
        {
            if (ReferenceEquals(_filterOverlay, overlay))
            {
                RootLayout.Children.Remove(overlay);
                _filterOverlay = null;
                _filterScrim = null;
                _filterSheet = null;
                SetBackgroundInteraction(enabled: true);
            }
            _filterClosing = false;
        }
    }

    private async void OnFilterClicked(object? sender, EventArgs e)
    {
        if (_filterOverlay is not null) return;
        ExerciseSearch.Unfocus();
        var thai = _viewModel.IsThai;
        var bodies = _viewModel.SelectedBodyParts.ToHashSet();
        var equipment = _viewModel.SelectedEquipment.ToHashSet();
        var refreshers = new List<Action>();
        var overlay = new Grid { BackgroundColor = Colors.Transparent, ZIndex = 10 };
        Grid.SetRowSpan(overlay, RootLayout.RowDefinitions.Count);
        var scrim = new BoxView { Color = Colors.Black, Opacity = _reduceMotion.IsEnabled ? 0.66 : 0 };
        var dismiss = new TapGestureRecognizer();
        dismiss.Tapped += async (_, _) => await CloseFiltersAsync();
        scrim.GestureRecognizers.Add(dismiss);
        overlay.Add(scrim);
        var sheet = new Border
        {
            BackgroundColor = Color.FromArgb("#181D21"), Stroke = Color.FromArgb("#343B40"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20, 20, 0, 0) },
            VerticalOptions = LayoutOptions.End,
            HeightRequest = Math.Min(Height * .78, 620), Padding = new Thickness(16, 8, 16, 16)
        };
        sheet.TranslationY = _reduceMotion.IsEnabled ? 0 : Math.Max(Height * .78, 420);
        var layout = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            RowSpacing = 12
        };
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(44) } };
        header.Add(new Label { Text = thai ? "ตัวกรอง" : "Filters", FontSize = 20, TextColor = Colors.White, VerticalTextAlignment = TextAlignment.Center });
        var close = new Button { Text = "×", FontSize = 26, BackgroundColor = Colors.Transparent, TextColor = Colors.White, HeightRequest = 44, Padding = 0 };
        close.Clicked += async (_, _) => await CloseFiltersAsync();
        header.Add(close, 1);
        layout.Add(header);
        var content = new VerticalStackLayout { Spacing = 12 };
        void Heading(string title, string helper)
        {
            content.Add(new Label { Text = title, FontSize = 17, TextColor = Colors.White, Margin = new Thickness(0, 8, 0, 0), FontFamily = "NotoSansThaiRegular" });
            content.Add(new Label { Text = helper, FontSize = 12, TextColor = Color.FromArgb("#B0B7BD") });
        }
        void Options<T>(IEnumerable<(T Value, string Label)> options, HashSet<T> selected, int columns) where T : notnull
        {
            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var index = 0;
            foreach (var (value, label) in options)
            {
                var row = index / columns;
                if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var button = new Button { FontSize = 14, FontFamily = "NotoSansThaiRegular", MinimumHeightRequest = 44, CornerRadius = 9, BorderWidth = 1, Padding = new Thickness(6, 8) };
                void Refresh()
                {
                    var active = selected.Contains(value);
                    button.Text = active ? label + "  ✓" : label;
                    button.BackgroundColor = Color.FromArgb(active ? "#C7FF32" : "#181D21");
                    button.TextColor = Color.FromArgb(active ? "#111800" : "#C2C8CD");
                    button.BorderColor = Color.FromArgb(active ? "#C7FF32" : "#687178");
                    SemanticProperties.SetDescription(button, label + (active ? (thai ? " เลือกแล้ว" : " selected") : ""));
                }
                refreshers.Add(Refresh);
                button.Clicked += (_, _) => { if (!selected.Add(value)) selected.Remove(value); Refresh(); };
                Refresh();
                grid.Add(button, index % columns, row);
                index++;
            }
            content.Add(grid);
        }
        Heading(thai ? "ส่วนที่ฝึก" : "Muscle groups", thai ? "เลือกได้มากกว่า 1 ส่วน" : "Choose one or more");
        Options(_viewModel.BodyPartOptions.Where(o => o.Value.HasValue).Select(o => (o.Value!.Value, o.Label)), bodies, 3);
        Heading(thai ? "อุปกรณ์" : "Equipment", thai ? "เลือกได้มากกว่า 1 แบบ" : "Choose one or more");
        Options(Enum.GetValues<ExerciseEquipment>().Select(v => (v, v.Label(thai))), equipment, 2);
        content.Add(new Label { Text = thai ? "แสดงท่าที่ตรงกับส่วนที่ฝึกและอุปกรณ์ที่เลือก" : "Match your selected muscles and equipment", FontSize = 12, TextColor = Color.FromArgb("#B0B7BD"), Margin = new Thickness(0, 8) });
        layout.Add(new ScrollView { Content = content }, 0, 1);
        var actions = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 };
        var clear = new Button { Text = thai ? "ล้างตัวกรอง" : "Clear", TextColor = Color.FromArgb("#C7FF32"), BackgroundColor = Colors.Transparent, FontSize = 14, HeightRequest = 48 };
        clear.Clicked += (_, _) => { bodies.Clear(); equipment.Clear(); foreach (var refresh in refreshers) refresh(); };
        var apply = new Button { Text = thai ? "แสดงผล" : "Show results", BackgroundColor = Color.FromArgb("#C7FF32"), TextColor = Color.FromArgb("#111800"), FontSize = 16, CornerRadius = 10, HeightRequest = 48 };
        apply.Clicked += async (_, _) => { _viewModel.ApplyFilters(bodies, equipment); await CloseFiltersAsync(); };
        actions.Add(clear);
        actions.Add(apply, 1);
        layout.Add(actions, 0, 2);
        sheet.Content = layout;
        overlay.Add(sheet);
        SetBackgroundInteraction(enabled: false);
        _filterOverlay = overlay;
        _filterScrim = scrim;
        _filterSheet = sheet;
        RootLayout.Add(overlay);
        if (!_reduceMotion.IsEnabled && Handler is not null)
        {
            await Task.WhenAll(
                scrim.FadeToAsync(0.66, 180, Easing.CubicOut),
                sheet.TranslateToAsync(0, 0, 240, Easing.CubicOut));
        }
    }

    private void OnSearchInputHandlerChanged(object? sender, EventArgs eventArgs)
    {
#if IOS
        if (ExerciseSearch.Handler?.PlatformView is UITextField input)
        {
            input.BorderStyle = UITextBorderStyle.None;
            input.BackgroundColor = UIColor.Clear;
        }
#endif
    }

    private void SetBackgroundInteraction(bool enabled)
    {
        foreach (var child in RootLayout.Children.OfType<VisualElement>().Where(child => !ReferenceEquals(child, _filterOverlay)))
        {
            child.InputTransparent = !enabled;
            AutomationProperties.SetExcludedWithChildren(child, !enabled);
        }
    }

    private async void OnCreateCustomClicked(object? sender, EventArgs eventArgs) =>
        await _navigator.OpenCustomExerciseAsync(_viewModel.SearchText.Trim());

    private async Task CompleteSelectionAsync()
    {
        if (AddingOverlay.IsVisible) return;
        var selected = _viewModel.SelectedExerciseIds.ToArray();
        if (selected.Length == 0)
        {
            await _warning.ShowAsync(
                this,
                _viewModel.Text.ExerciseSelectionRequiredTitle,
                _viewModel.Text.ExerciseSelectionRequiredMessage,
                _viewModel.Text.Okay);
            return;
        }
        ExerciseSearch.Unfocus();
        AddingOverlay.IsVisible = true;
        try
        {
            var callbacks = SelectionCompleted?.GetInvocationList()
                .Cast<Func<IReadOnlyCollection<Guid>, Task>>()
                .ToArray() ?? [];
            foreach (var callback in callbacks)
                await callback(selected);
            EndPickerDraft();
            await _navigator.ReturnToWorkoutAsync();
        }
        finally { AddingOverlay.IsVisible = false; }
    }

    private async void EditCustomExercise(CachedExercise exercise) =>
        await Shell.Current.GoToAsync($"{nameof(CustomExercisePage)}?exerciseId={exercise.Id:D}");

    private async Task OpenTechniqueAsync(object? parameter)
    {
        if (parameter is ExercisePickerItem item)
            await _navigator.OpenTechniqueAsync(item.Id);
    }

    private void EndPickerDraft()
    {
        _viewModel.ResetSelection();
        _hasActivePickerRequest = false;
        _loaded = false;
    }
}
