namespace TrackZ.Mobile.Features.Workout;

public interface ISetSavedPulseDriver
{
    Task InvokeAsync(Func<Task> action);
    Task StartAsync(SetSavedOutcome outcome, CancellationToken cancellationToken);
    void Cancel();
}

public interface IReduceMotionPreference
{
    bool IsEnabled { get; }
}

public sealed class MauiReduceMotionPreference : IReduceMotionPreference
{
    private readonly Func<bool> _userSetting;
    private readonly Func<bool> _operatingSystemSetting;

    public MauiReduceMotionPreference()
        : this(ReadUserSetting, ReadOperatingSystemSetting)
    {
    }

    public MauiReduceMotionPreference(
        Func<bool> userSetting,
        Func<bool> operatingSystemSetting)
    {
        ArgumentNullException.ThrowIfNull(userSetting);
        ArgumentNullException.ThrowIfNull(operatingSystemSetting);
        _userSetting = userSetting;
        _operatingSystemSetting = operatingSystemSetting;
    }

    public bool IsEnabled => _userSetting() || _operatingSystemSetting();

    private static bool ReadUserSetting() =>
        Preferences.Default.Get("trackz_reduce_motion", false);

    private static bool ReadOperatingSystemSetting()
    {
#if IOS
        return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26)
                && !Android.Animation.ValueAnimator.AreAnimatorsEnabled())
                return true;
            var resolver = Android.App.Application.Context.ContentResolver;
            return Android.Provider.Settings.Global.GetFloat(
                resolver,
                Android.Provider.Settings.Global.AnimatorDurationScale,
                1f) == 0f;
        }
        catch (Exception)
        {
            return false;
        }
#else
        return false;
#endif
    }
}

public sealed class MauiSetSavedPulseDriver : ISetSavedPulseDriver
{
    private readonly VisualElement _pulse;
    private readonly Presentation.ITrackZMotion _motion;

    public MauiSetSavedPulseDriver(
        VisualElement pulse,
        IReduceMotionPreference? reduceMotion = null,
        Presentation.ITrackZMotion? motion = null)
    {
        ArgumentNullException.ThrowIfNull(pulse);
        _pulse = pulse;
        _motion = motion ?? new Presentation.MauiTrackZMotion(
            reduceMotion ?? new MauiReduceMotionPreference());
    }

    public Task InvokeAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);

    public Task StartAsync(SetSavedOutcome outcome, CancellationToken cancellationToken) =>
        _motion.PlaySetSavedAsync(_pulse, outcome, cancellationToken);

    public void Cancel() => _motion.Cancel(_pulse);
}
