using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Workout;

namespace TrackZ.Mobile.Tests.History;

public sealed class WorkoutHistoryDetailViewModelTests
{
    [Fact]
    public void History_list_rows_are_collapsed_and_reconciling_detail_is_destructive_action_blocked()
    {
        var row = new HistoryWorkoutItem(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            false,
            [],
            WorkoutSyncState.Reconciling,
            "Reconciling",
            null,
            null,
            null);

        Assert.False(row.IsExpanded);
        Assert.True(row.ActionsBlocked);
        Assert.False(WorkoutHistoryDetailViewModel.CanResolveDestructively(row));
    }
}
