using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Features.Profile;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _viewModel;
    private readonly IWeightUnitPreference _units;
    private bool _unitsSubscribed;
    public ProfilePage(
        ProfileViewModel viewModel,
        IWeightUnitPreference units,
        ProfileSignOutController signOut,
        WorkoutTextSet workoutText)
    {
        _viewModel = viewModel;
        _units = units;
        SignOut = signOut;
        WorkoutText = workoutText;
        InitializeComponent();
        BindingContext = _viewModel;
    }
    public WorkoutTextSet WorkoutText { get; }
    public ProfileSignOutController SignOut { get; }
    public bool IsKilogramsSelected => _units.Current == WeightDisplayUnit.Kilograms;
    public bool IsPoundsSelected => _units.Current == WeightDisplayUnit.Pounds;
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SubscribeUnits();
        RefreshUnitSelection();
        HapticsSwitch.IsToggled = Preferences.Default.Get("trackz_haptics_enabled", true);
        ReduceMotionSwitch.IsToggled = Preferences.Default.Get("trackz_reduce_motion", false);
        await _viewModel.LoadAsync();
    }
    protected override void OnDisappearing()
    {
        UnsubscribeUnits();
        base.OnDisappearing();
    }
    private void OnKilogramsClicked(object? sender, EventArgs args) => _units.Set(WeightDisplayUnit.Kilograms);
    private void OnPoundsClicked(object? sender, EventArgs args) => _units.Set(WeightDisplayUnit.Pounds);
    private void OnHapticsToggled(object? sender, ToggledEventArgs args) => Preferences.Default.Set("trackz_haptics_enabled", args.Value);
    private void OnReduceMotionToggled(object? sender, ToggledEventArgs args) => Preferences.Default.Set("trackz_reduce_motion", args.Value);

    private void SubscribeUnits()
    {
        if (_unitsSubscribed) return;
        _units.Changed += OnUnitsChanged;
        _unitsSubscribed = true;
    }

    private void UnsubscribeUnits()
    {
        if (!_unitsSubscribed) return;
        _units.Changed -= OnUnitsChanged;
        _unitsSubscribed = false;
    }

    private void OnUnitsChanged(object? sender, EventArgs args) => RefreshUnitSelection();

    private void RefreshUnitSelection()
    {
        OnPropertyChanged(nameof(IsKilogramsSelected));
        OnPropertyChanged(nameof(IsPoundsSelected));
    }
}
