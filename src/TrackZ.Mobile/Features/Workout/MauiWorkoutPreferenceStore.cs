namespace TrackZ.Mobile.Features.Workout;

public sealed class MauiWorkoutPreferenceStore : IWorkoutPreferenceStore
{
    public string? Get(string key) => Preferences.Default.Get<string?>(key, null);
    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
