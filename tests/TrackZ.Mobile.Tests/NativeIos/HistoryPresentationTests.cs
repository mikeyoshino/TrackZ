using TrackZ.Mobile.Features.History;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class HistoryPresentationTests
{
    [Fact]
    public void History_uses_pushed_detail_and_native_operation_sheets()
    {
        Assert.True(typeof(WorkoutHistoryDetailPage).IsSubclassOf(typeof(ContentPage)));
        Assert.True(typeof(HistorySetEditorSheetPage).IsSubclassOf(typeof(ContentPage)));
        Assert.True(typeof(HistoryConflictSheetPage).IsSubclassOf(typeof(ContentPage)));
    }
}
