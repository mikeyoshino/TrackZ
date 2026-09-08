using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Features.Exercises.Models;

namespace TrackZ.Mobile.Tests.Exercises;

public sealed class BundledExerciseArtworkTests
{
    [Fact]
    public void Supplemental_barbell_press_has_offline_artwork_without_server_thumbnail()
    {
        var exercise = Create();
        Assert.Equal("seated_barbell_shoulder_press.png", exercise.ThumbnailUri);
    }

    [Fact]
    public void Custom_or_downloadable_artwork_is_not_replaced_by_bundled_fallback()
    {
        Assert.Null(Create(custom: true).ThumbnailUri);
        Assert.Null(Create(remote: "/api/v1/images/thumbnail").ThumbnailUri);
        Assert.Equal("cached.png", Create(local: "cached.png").ThumbnailUri);
    }

    private static CachedExercise Create(bool custom = false, string? local = null, string? remote = null) => new()
    {
        Id = SupplementalExercises.SeatedBarbellShoulderPressId,
        Name = "Seated Barbell Shoulder Press",
        BodyPart = BodyPart.Shoulders,
        TrackingMode = TrackingMode.Weighted,
        IsCustom = custom,
        ThumbnailUri = local,
        RemoteThumbnailRoute = remote
    };
}
