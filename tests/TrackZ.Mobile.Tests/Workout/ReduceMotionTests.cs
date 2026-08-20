using Microsoft.Maui.Controls;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.Workout;

public sealed class ReduceMotionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Preference_combines_persisted_user_and_operating_system_settings(
        bool userEnabled,
        bool operatingSystemEnabled,
        bool expected)
    {
        var preference = new MauiReduceMotionPreference(
            () => userEnabled,
            () => operatingSystemEnabled);

        Assert.Equal(expected, preference.IsEnabled);
    }

    [Fact]
    public async Task Reduced_motion_pulse_sets_final_state_without_running_an_animation()
    {
        var pulse = new BoxView { Opacity = 0.4, Scale = 0.7 };
        var driver = new MauiSetSavedPulseDriver(
            pulse,
            new FixedReduceMotionPreference(isEnabled: true));

        await driver.StartAsync(SetSavedOutcome.PersonalRecord, CancellationToken.None);

        Assert.Equal(0, pulse.Opacity);
        Assert.Equal(1, pulse.Scale);
        Assert.False(pulse.AnimationIsRunning("FadeTo"));
    }

    private sealed class FixedReduceMotionPreference(bool isEnabled) : IReduceMotionPreference
    {
        public bool IsEnabled { get; } = isEnabled;
    }
}
