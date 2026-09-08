using TrackZ.Mobile.Features.Progress;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class MuscleCoveragePageTests
{
    [Fact]
    public void No_record_copy_uses_readable_secondary_text_without_changing_anatomy_fill()
    {
        Assert.Equal(Color.FromArgb("#A7AFB8"), MuscleCoveragePage.StatusTextColor(TrackZ.Domain.Muscles.MuscleTrainingStatus.NoRecord));
        Assert.Equal(Color.FromArgb("#626B72"), MuscleBodyDiagram.StatusColor(TrackZ.Domain.Muscles.MuscleTrainingStatus.NoRecord));
    }

    [Fact]
    public void Drilldown_diagrams_leave_room_for_the_list_and_primary_action()
    {
        Assert.Equal(185, MuscleCoveragePage.DiagramHeight(TrackZ.Domain.Exercises.BodyPart.Legs, null));
        Assert.Equal(190, MuscleCoveragePage.DiagramHeight(TrackZ.Domain.Exercises.BodyPart.Legs, "quads"));
    }

    [Fact]
    public void Recommendation_controls_are_compact_but_keep_a_full_touch_target()
    {
        var thumbnail = MuscleCoveragePage.RecommendationThumbnail("exercise_placeholder.png");
        var selector = MuscleCoveragePage.RecommendationSelector(false, "Select exercise Squat", () => Task.CompletedTask);

        Assert.Equal(72, thumbnail.WidthRequest);
        Assert.Equal(72, thumbnail.HeightRequest);
        Assert.Equal(44, selector.WidthRequest);
        Assert.Equal(44, selector.HeightRequest);
        Assert.Equal(0, selector.BorderWidth);
        Assert.Equal(LayoutOptions.Center, selector.HorizontalOptions);
        Assert.Equal(LayoutOptions.Center, selector.VerticalOptions);
    }

    [Fact]
    public void Add_action_uses_disabled_surface_until_an_exercise_is_selected()
    {
        var button = MuscleCoveragePage.AddExerciseButton(hasSelection: false, adding: false, () => Task.CompletedTask);

        Assert.False(button.IsEnabled);
        Assert.Equal(Color.FromArgb("#252B31"), button.BackgroundColor);
        Assert.Equal(Color.FromArgb("#68717B"), button.TextColor);
    }

    [Fact]
    public void Largest_text_stacks_group_status_below_the_region_name()
    {
        var row = MuscleCoveragePage.RegionRowContent("Quadriceps", "3 primary sets", Color.FromArgb("#C8FF3D"), largeText: true);
        var status = Assert.IsType<Label>(row.Children[1]);

        Assert.Equal(2, row.RowDefinitions.Count);
        Assert.Equal(0, Grid.GetColumn(status));
        Assert.Equal(1, Grid.GetRow(status));
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(1, false, 1)]
    [InlineData(5, false, 2)]
    [InlineData(5, true, 5)]
    public void Recommendation_list_starts_with_two_and_expands_on_request(
        int available,
        bool expanded,
        int expected)
    {
        Assert.Equal(expected, MuscleCoveragePage.VisibleRecommendationCount(available, expanded));
    }
}
