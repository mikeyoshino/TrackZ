using TrackZ.Contracts.Exercises;
using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Tests.Exercises;

public sealed class ExerciseDefinitionTests
{
    [Theory]
    [InlineData(BodyPart.Chest, 1)]
    [InlineData(BodyPart.Back, 2)]
    [InlineData(BodyPart.Shoulders, 3)]
    [InlineData(BodyPart.Arms, 4)]
    [InlineData(BodyPart.Legs, 5)]
    [InlineData(BodyPart.Core, 6)]
    public void Body_part_wire_values_are_stable(BodyPart bodyPart, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)bodyPart);
    }

    [Theory]
    [InlineData(TrackingMode.Weighted, 1)]
    [InlineData(TrackingMode.Bodyweight, 2)]
    [InlineData(TrackingMode.Assisted, 3)]
    public void Tracking_mode_wire_values_are_stable(TrackingMode trackingMode, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)trackingMode);
    }

    [Theory]
    [InlineData(ExerciseImageReviewState.Draft, 1)]
    [InlineData(ExerciseImageReviewState.Reviewed, 2)]
    [InlineData(ExerciseImageReviewState.Published, 3)]
    public void Image_review_state_wire_values_are_stable(ExerciseImageReviewState reviewState, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)reviewState);
    }

    [Theory]
    [InlineData(ExerciseImageSource.SystemArtwork, 1)]
    [InlineData(ExerciseImageSource.UserUpload, 2)]
    public void Image_source_wire_values_are_stable(ExerciseImageSource source, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)source);
    }

    [Fact]
    public void System_exercise_has_no_owner_and_normalizes_its_name()
    {
        var createdAt = new DateTimeOffset(2026, 8, 15, 10, 30, 0, TimeSpan.FromHours(7));

        var exercise = ExerciseDefinition.CreateSystem("  Incline Barbell Bench Press  ", BodyPart.Chest, TrackingMode.Weighted, createdAt);

        Assert.Null(exercise.OwnerId);
        Assert.True(exercise.IsSystem);
        Assert.False(exercise.IsCustom);
        Assert.Equal("Incline Barbell Bench Press", exercise.Name);
        Assert.Equal("INCLINE BARBELL BENCH PRESS", exercise.NormalizedName);
        Assert.Equal(createdAt.ToUniversalTime(), exercise.CreatedAt);
    }

    [Fact]
    public void Custom_exercise_requires_owner()
    {
        Assert.Throws<ArgumentException>(() =>
            ExerciseDefinition.CreateCustom(Guid.Empty, "My Press", BodyPart.Chest, TrackingMode.Weighted));
    }

    [Fact]
    public void Custom_exercise_is_owned_by_its_creator()
    {
        var ownerId = Guid.NewGuid();

        var exercise = ExerciseDefinition.CreateCustom(ownerId, "My Press", BodyPart.Chest, TrackingMode.Weighted);

        Assert.Equal(ownerId, exercise.OwnerId);
        Assert.True(exercise.IsCustom);
        Assert.False(exercise.IsSystem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Exercise_name_must_not_be_blank(string name)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExerciseDefinition.CreateSystem(name, BodyPart.Chest, TrackingMode.Weighted));
    }

    [Fact]
    public void Exercise_name_is_limited_to_100_characters_after_trimming()
    {
        var longestLegalName = new string('a', 100);
        var tooLongName = new string('a', 101);

        var exercise = ExerciseDefinition.CreateSystem(longestLegalName, BodyPart.Chest, TrackingMode.Weighted);

        Assert.Equal(longestLegalName, exercise.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExerciseDefinition.CreateSystem(tooLongName, BodyPart.Chest, TrackingMode.Weighted));
    }

    [Fact]
    public void Custom_exercise_can_be_archived()
    {
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "My Press", BodyPart.Chest, TrackingMode.Weighted);

        exercise.Archive();

        Assert.True(exercise.IsArchived);
    }

    [Fact]
    public void System_exercise_cannot_be_archived()
    {
        var exercise = ExerciseDefinition.CreateSystem("Bench Press", BodyPart.Chest, TrackingMode.Weighted);

        Assert.Throws<InvalidOperationException>(exercise.Archive);
        Assert.False(exercise.IsArchived);
    }

    [Fact]
    public void Tracking_mode_can_change_before_set_history_is_recorded()
    {
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "My Press", BodyPart.Chest, TrackingMode.Weighted);

        exercise.ChangeTrackingMode(TrackingMode.Bodyweight);

        Assert.Equal(TrackingMode.Bodyweight, exercise.TrackingMode);
        Assert.False(exercise.HasSetHistory);
    }

    [Fact]
    public void Tracking_mode_cannot_change_after_set_history_is_recorded()
    {
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "My Press", BodyPart.Chest, TrackingMode.Weighted);
        exercise.RecordSetHistory();

        Assert.Throws<InvalidOperationException>(() =>
            exercise.ChangeTrackingMode(TrackingMode.Bodyweight));

        Assert.Equal(TrackingMode.Weighted, exercise.TrackingMode);
    }

    [Fact]
    public void Recording_set_history_is_irreversible_and_cannot_be_bypassed_by_a_boolean_parameter()
    {
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "My Press", BodyPart.Chest, TrackingMode.Weighted);

        exercise.RecordSetHistory();
        exercise.RecordSetHistory();

        Assert.True(exercise.HasSetHistory);
        var changeMethod = typeof(ExerciseDefinition).GetMethod(nameof(ExerciseDefinition.ChangeTrackingMode));
        Assert.NotNull(changeMethod);
        Assert.Single(changeMethod.GetParameters());
        Assert.NotEqual(typeof(bool), changeMethod.GetParameters()[0].ParameterType);
        Assert.False(typeof(ExerciseDefinition).GetProperty(nameof(ExerciseDefinition.HasSetHistory))!.SetMethod?.IsPublic == true);
    }

    [Fact]
    public void System_image_starts_draft_with_system_visibility_and_metadata()
    {
        var createdAt = new DateTimeOffset(2026, 8, 15, 10, 30, 0, TimeSpan.FromHours(7));
        var exercise = CreateSystemExercise(createdAt);

        var image = ExerciseImage.CreateSystem(
            exercise,
            "system/incline-bench/master.webp",
            "system/incline-bench/thumb.webp",
            version: 1,
            sourceReference: "commission-2026-08",
            createdAt: createdAt);

        Assert.Equal(exercise.Id, image.ExerciseDefinitionId);
        Assert.Null(image.OwnerId);
        Assert.False(image.IsPrivate);
        Assert.Equal(ExerciseImageSource.SystemArtwork, image.Source);
        Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState);
        Assert.Equal(createdAt.ToUniversalTime(), image.CreatedAt);
    }

    [Fact]
    public void Custom_upload_is_private_and_owned_by_its_creator()
    {
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "My Press", BodyPart.Chest, TrackingMode.Weighted);

        var image = ExerciseImage.CreateCustomUpload(
            exercise, ownerId, "users/owner/master.webp", "users/owner/thumb.webp", version: 1, sourceReference: "upload-1");

        Assert.Equal(ownerId, image.OwnerId);
        Assert.True(image.IsPrivate);
        Assert.Equal(ExerciseImageSource.UserUpload, image.Source);
        Assert.True(image.IsReadyForUse);
        Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
    }

    [Fact]
    public void System_artwork_cannot_be_created_for_a_custom_exercise()
    {
        var customExercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "My Press", BodyPart.Chest, TrackingMode.Weighted);

        Assert.Throws<ArgumentException>(() => ExerciseImage.CreateSystem(
            customExercise, "system/master.webp", "system/thumb.webp", version: 1, sourceReference: "commission-2026-08"));
    }

    [Fact]
    public void Private_upload_cannot_be_created_for_a_system_exercise()
    {
        Assert.Throws<ArgumentException>(() => ExerciseImage.CreateCustomUpload(
            CreateSystemExercise(), Guid.NewGuid(), "users/owner/master.webp", "users/owner/thumb.webp", version: 1, sourceReference: "upload-1"));
    }

    [Fact]
    public void Private_upload_must_match_the_custom_exercise_owner()
    {
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "My Press", BodyPart.Chest, TrackingMode.Weighted);

        Assert.Throws<ArgumentException>(() => ExerciseImage.CreateCustomUpload(
            exercise, Guid.NewGuid(), "users/other/master.webp", "users/other/thumb.webp", version: 1, sourceReference: "upload-1"));
    }

    [Fact]
    public void Image_cannot_be_published_before_review()
    {
        var image = CreateDraftSystemImage();

        Assert.Throws<InvalidOperationException>(() => image.Publish(DateTimeOffset.UtcNow));
        Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState);
    }

    [Fact]
    public void System_image_review_rejects_each_missing_human_approval()
    {
        Assert.Throws<ArgumentException>(() => CreateDraftSystemImage().Review(Guid.Empty, "rights-2026", true, true, true, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => CreateDraftSystemImage().Review(Guid.NewGuid(), "rights-2026", anatomyApproved: false, movementApproved: true, rightsApproved: true, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => CreateDraftSystemImage().Review(Guid.NewGuid(), "rights-2026", anatomyApproved: true, movementApproved: false, rightsApproved: true, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => CreateDraftSystemImage().Review(Guid.NewGuid(), "rights-2026", anatomyApproved: true, movementApproved: true, rightsApproved: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reviewed_system_image_can_be_published_and_records_approval_metadata_in_utc()
    {
        var image = CreateDraftSystemImage();
        var reviewerId = Guid.NewGuid();
        var reviewedAt = new DateTimeOffset(2026, 8, 15, 10, 30, 0, TimeSpan.FromHours(7));
        var publishedAt = reviewedAt.AddMinutes(5);

        image.Review(reviewerId, "rights-2026", anatomyApproved: true, movementApproved: true, rightsApproved: true, reviewedAt);
        image.Publish(publishedAt);

        Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
        Assert.Equal(reviewerId, image.ReviewedByUserId);
        Assert.Equal("rights-2026", image.RightsReference);
        Assert.True(image.AnatomyApproved);
        Assert.True(image.MovementApproved);
        Assert.True(image.RightsApproved);
        Assert.Equal(reviewedAt.ToUniversalTime(), image.ReviewedAt);
        Assert.Equal(publishedAt.ToUniversalTime(), image.PublishedAt);
    }

    [Fact]
    public void Private_custom_image_cannot_be_reviewed_or_published()
    {
        var ownerId = Guid.NewGuid();
        var image = ExerciseImage.CreateCustomUpload(
            ExerciseDefinition.CreateCustom(ownerId, "My Press", BodyPart.Chest, TrackingMode.Weighted), ownerId,
            "users/owner/master.webp", "users/owner/thumb.webp", version: 1, sourceReference: "upload-1");

        Assert.Throws<InvalidOperationException>(() => image.Review(Guid.NewGuid(), "rights-2026", anatomyApproved: true, movementApproved: true, rightsApproved: true, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => image.Publish(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void System_image_cannot_be_reviewed_before_its_creation_time()
    {
        var createdAt = new DateTimeOffset(2026, 8, 15, 10, 30, 0, TimeSpan.FromHours(7));
        var image = ExerciseImage.CreateSystem(
            CreateSystemExercise(createdAt), "system/master.webp", "system/thumb.webp", version: 1, sourceReference: "commission-2026-08", createdAt: createdAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.Review(
            Guid.NewGuid(), "rights-2026", anatomyApproved: true, movementApproved: true, rightsApproved: true, createdAt.AddMinutes(-1)));
    }

    [Fact]
    public void Image_cannot_be_published_before_its_recorded_review_time()
    {
        var image = CreateDraftSystemImage();
        var reviewedAt = DateTimeOffset.UtcNow;

        image.Review(Guid.NewGuid(), "rights-2026", anatomyApproved: true, movementApproved: true, rightsApproved: true, reviewedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.Publish(reviewedAt.AddMinutes(-1)));
        Assert.Equal(ExerciseImageReviewState.Reviewed, image.ReviewState);
        Assert.Null(image.PublishedAt);
    }

    [Fact]
    public void Exercise_summary_contract_is_immutable_and_normalizes_performance_timestamp_to_utc()
    {
        var performedAt = new DateTimeOffset(2026, 8, 15, 10, 30, 0, TimeSpan.FromHours(7));
        var summary = new ExerciseSummaryDto(
            Guid.NewGuid(),
            "Bench Press",
            BodyPart.Chest,
            TrackingMode.Weighted,
            "https://cdn.trackz.app/bench-thumb.webp",
            performedAt,
            new PerformanceSetDto(80m, null, 5),
            new PerformanceSetDto(100m, null, 1),
            isCustom: false);

        Assert.Equal(performedAt.ToUniversalTime(), summary.LastPerformedAt);
        Assert.All(typeof(ExerciseSummaryDto).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic == true));
        Assert.All(typeof(PerformanceSetDto).GetProperties(), property => Assert.False(property.SetMethod?.IsPublic == true));
    }

    private static ExerciseImage CreateDraftSystemImage() => ExerciseImage.CreateSystem(
        CreateSystemExercise(),
        "system/incline-bench/master.webp",
        "system/incline-bench/thumb.webp",
        version: 1,
        sourceReference: "commission-2026-08");

    private static ExerciseDefinition CreateSystemExercise(DateTimeOffset? createdAt = null) => ExerciseDefinition.CreateSystem(
        "Incline Barbell Bench Press",
        BodyPart.Chest,
        TrackingMode.Weighted,
        createdAt);
}
