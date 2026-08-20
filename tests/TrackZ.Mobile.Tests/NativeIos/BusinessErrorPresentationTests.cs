using System.Globalization;
using TrackZ.Contracts.Errors;
using TrackZ.Mobile.Features.Shared;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class BusinessErrorPresentationTests
{
    [Theory]
    [InlineData(BusinessErrorCode.WorkoutNotFound, "WorkoutNotFound")]
    [InlineData(BusinessErrorCode.InvalidSetValue, "InvalidSetValue")]
    [InlineData(BusinessErrorCode.VersionConflict, "SyncConflict")]
    public void Stable_codes_map_to_localized_copy_without_rendering_the_numeric_code(
        BusinessErrorCode code,
        string resourceKey)
    {
        var presentation = BusinessErrorPresenter.Map(code);
        var thai = BusinessErrorText.Resolve(presentation.ResourceKey, CultureInfo.GetCultureInfo("th-TH"));
        Assert.Equal(resourceKey, presentation.ResourceKey);
        Assert.DoesNotContain(((int)code).ToString(CultureInfo.InvariantCulture), thai, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(thai));
    }
}
