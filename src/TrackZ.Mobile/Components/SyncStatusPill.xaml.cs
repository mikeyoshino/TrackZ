using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Components;

public partial class SyncStatusPill : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(SyncStatusPill), string.Empty);
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(WorkoutSyncState), typeof(SyncStatusPill), WorkoutSyncState.Synced,
        propertyChanged: OnStateChanged);

    public SyncStatusPill()
    {
        InitializeComponent();
        ApplyState(WorkoutSyncState.Synced);
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public WorkoutSyncState State { get => (WorkoutSyncState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SyncStatusPill)bindable).ApplyState((WorkoutSyncState)newValue);

    private void ApplyState(WorkoutSyncState state)
    {
        Pill.BackgroundColor = state switch
        {
            WorkoutSyncState.Conflicted => Color.FromArgb("#3A2025"),
            WorkoutSyncState.PermanentFailure => Color.FromArgb("#491D25"),
            WorkoutSyncState.Offline => Color.FromArgb("#292D34"),
            WorkoutSyncState.Pending => Color.FromArgb("#31321F"),
            WorkoutSyncState.Syncing => Color.FromArgb("#192A3D"),
            WorkoutSyncState.Reconciling => Color.FromArgb("#473617"),
            _ => Color.FromArgb("#173126")
        };
    }
}
