using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class SetEntrySheetTests
{
    [Fact]
    public void Set_entry_is_a_native_page_and_motion_has_outcome_specific_contract()
    {
        Assert.True(typeof(SetEntrySheetPage).IsSubclassOf(typeof(ContentPage)));
        Assert.Contains(
            typeof(SetSavedOutcome),
            typeof(ITrackZMotion).GetMethod(nameof(ITrackZMotion.PlaySetSavedAsync))!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
    }
}
