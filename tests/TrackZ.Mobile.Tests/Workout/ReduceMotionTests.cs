using Microsoft.Maui.Controls;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Presentation;

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

    [Fact]
    public void Saved_feedback_is_accessible_only_while_it_is_visible()
    {
        var pulse = new BoxView();

        MauiTrackZMotion.SetFeedbackVisibility(pulse, isVisible: true);
        Assert.False(pulse.InputTransparent);
        Assert.False((bool)pulse.GetValue(AutomationProperties.ExcludedWithChildrenProperty));

        MauiTrackZMotion.SetFeedbackVisibility(pulse, isVisible: false);
        Assert.True(pulse.InputTransparent);
        Assert.True((bool)pulse.GetValue(AutomationProperties.ExcludedWithChildrenProperty));
    }

    private sealed class FixedReduceMotionPreference(bool isEnabled) : IReduceMotionPreference
    {
        public bool IsEnabled { get; } = isEnabled;
    }
}
