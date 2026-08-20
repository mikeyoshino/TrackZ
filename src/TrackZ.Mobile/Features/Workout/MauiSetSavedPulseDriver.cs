namespace TrackZ.Mobile.Features.Workout;

public interface ISetSavedPulseDriver
{
    Task InvokeAsync(Func<Task> action);
    Task StartAsync(CancellationToken cancellationToken);
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
    private readonly IReduceMotionPreference _reduceMotion;

    public MauiSetSavedPulseDriver(
        VisualElement pulse,
        IReduceMotionPreference? reduceMotion = null)
    {
        ArgumentNullException.ThrowIfNull(pulse);
        _pulse = pulse;
        _reduceMotion = reduceMotion ?? new MauiReduceMotionPreference();
    }

    public Task InvokeAsync(Func<Task> action) => MainThread.InvokeOnMainThreadAsync(action);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pulse.CancelAnimations();
        if (_reduceMotion.IsEnabled)
        {
            _pulse.Scale = 1;
            _pulse.Opacity = 0;
            return;
        }
        _pulse.Opacity = 1;
        _pulse.Scale = 0.97;
        await Task.WhenAll(
            _pulse.ScaleToAsync(1, 160, Easing.CubicOut),
            _pulse.FadeToAsync(0, 520, Easing.CubicIn));
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Cancel() => MainThread.BeginInvokeOnMainThread(_pulse.CancelAnimations);
}
