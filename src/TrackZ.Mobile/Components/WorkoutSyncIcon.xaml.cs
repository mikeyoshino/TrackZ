using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Components;

public partial class WorkoutSyncIcon : ContentView
{
    public static readonly BindableProperty AccessibilityTextProperty = BindableProperty.Create(
        nameof(AccessibilityText), typeof(string), typeof(WorkoutSyncIcon), string.Empty);
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(WorkoutSyncState), typeof(WorkoutSyncIcon), WorkoutSyncState.Synced,
        propertyChanged: OnStateChanged);

    public WorkoutSyncIcon()
    {
        InitializeComponent();
        ApplyState(WorkoutSyncState.Synced);
    }

    public string AccessibilityText
    {
        get => (string)GetValue(AccessibilityTextProperty);
        set => SetValue(AccessibilityTextProperty, value);
    }

    public WorkoutSyncState State
    {
        get => (WorkoutSyncState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((WorkoutSyncIcon)bindable).ApplyState((WorkoutSyncState)newValue);

    private void ApplyState(WorkoutSyncState state)
    {
        var isSynced = state == WorkoutSyncState.Synced;
        SyncIcon.SetDynamicResource(
            Label.TextColorProperty,
            isSynced ? "TrackZPrimary" : "TrackZTextSecondary");
        SyncCheck.IsVisible = isSynced;
    }
}
