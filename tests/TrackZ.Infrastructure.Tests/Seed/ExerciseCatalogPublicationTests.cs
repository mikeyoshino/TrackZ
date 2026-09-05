using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Workouts;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Persistence.Seed;
using TrackZ.Infrastructure.Tests.Persistence;

namespace TrackZ.Infrastructure.Tests.Seed;

[Collection(ExerciseCatalogPublicationCollection.Name)]
public sealed class ExerciseCatalogPublicationTests(ExerciseCatalogPublicationFixture fixture)
{
    private const int CatalogExerciseCount = ExerciseManifest.SystemExerciseCount;
    private const int CatalogObjectCount = CatalogExerciseCount * 2;
    private const int ConcurrentVerificationReadCount = CatalogObjectCount * 2;
    private static readonly Guid ReviewerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherReviewerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string RightsReference = "local-simulator-review-2026-08-21";
    private static readonly HashSet<string> ExpansionExerciseNames =
    [
        "Push-Up", "Chest Dip", "Decline Barbell Bench Press", "Smith Machine Bench Press",
        "Dumbbell Fly", "Low-to-High Cable Fly", "High-to-Low Cable Fly",
        "Conventional Deadlift", "T-Bar Row", "Inverted Row", "Neutral-Grip Lat Pulldown",
        "Wide-Grip Lat Pulldown", "Single-Arm Cable Row", "Machine High Row",
        "Arnold Press", "Dumbbell Front Raise", "Cable Front Raise", "Bent-Over Reverse Fly",
        "Reverse Pec Deck", "Landmine Press", "Dumbbell Shrug",
        "EZ-Bar Curl", "Incline Dumbbell Curl", "Cable Curl", "Concentration Curl", "Bench Dip",
        "Triceps Dip", "Single-Arm Cable Pushdown",
        "Goblet Squat", "Hack Squat", "Sumo Deadlift", "Walking Lunge", "Hip Thrust",
        "Lying Leg Curl", "Seated Calf Raise",
        "Plank", "Side Plank", "Dead Bug", "Bird Dog", "Russian Twist", "Bicycle Crunch",
        "Mountain Climber"
    ];

    [Fact]
    public async Task Exact_90_drafts_publish_atomically_with_complete_review_metadata()
    {
        var scenario = await fixture.PrepareAsync();

        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);

        var images = await scenario.Database.ExerciseImages.AsNoTracking()
            .OrderBy(image => image.ExerciseDefinitionId)
            .ToArrayAsync();
        Assert.Equal(CatalogExerciseCount, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
            Assert.True(image.AnatomyApproved && image.MovementApproved && image.RightsApproved);
            Assert.True(image.IsReadyForUse);
            Assert.Equal(scenario.Now, image.ReviewedAt);
            Assert.Equal(scenario.Now, image.PublishedAt);
        });
    }

    [Fact]
    public async Task Concurrent_publications_serialize_and_preserve_the_first_committed_metadata()
    {
        var scenario = await fixture.PrepareAsync();
        var barrier = new ConcurrentPublicationBarrier();
        var firstTime = barrier.CreateTimeProvider(scenario.Now.AddMinutes(1));
        var secondTime = barrier.CreateTimeProvider(scenario.Now.AddMinutes(2));
        await using var firstContext = fixture.Database.CreateDbContext();
        await using var secondContext = fixture.Database.CreateDbContext();
        var first = new ExerciseCatalogPublicationService(firstContext, scenario.Deployment, firstTime);
        var second = new ExerciseCatalogPublicationService(secondContext, scenario.Deployment, secondTime);

        var monitor = ReleaseBarrierWhenSerializedAsync(barrier, fixture.Database.ConnectionString);
        var attempts = await Task.WhenAll(
            CapturePublicationAsync(first, firstTime, ReviewerId, RightsReference),
            CapturePublicationAsync(second, secondTime, OtherReviewerId, "other-reviewed-rights"));
        await monitor;

        Assert.All(attempts, attempt => Assert.Null(attempt.Error));
        var winnerTime = Assert.Single(new[] { firstTime, secondTime }, time => time.WasRead);
        var winner = Assert.Single(attempts, attempt => attempt.TimeProvider == winnerTime);
        await using var verificationContext = fixture.Database.CreateDbContext();
        var images = await verificationContext.ExerciseImages.AsNoTracking()
            .OrderBy(image => image.ExerciseDefinitionId)
            .ToArrayAsync();
        Assert.Equal(CatalogExerciseCount, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(winner.ReviewerId, image.ReviewedByUserId);
            Assert.Equal(winner.RightsReference, image.RightsReference);
            Assert.True(image.AnatomyApproved && image.MovementApproved && image.RightsApproved);
            Assert.True(image.IsReadyForUse);
            Assert.Equal(winner.TimeProvider.GetConfiguredUtcNow(), image.ReviewedAt);
            Assert.Equal(winner.TimeProvider.GetConfiguredUtcNow(), image.PublishedAt);
        });
        Assert.Equal(ConcurrentVerificationReadCount, scenario.Storage.GetCount);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
    }

    [Fact]
    public async Task Concurrent_matching_publications_are_idempotent_without_timestamp_overwrite()
    {
        var scenario = await fixture.PrepareAsync();
        var barrier = new ConcurrentPublicationBarrier();
        var firstTime = barrier.CreateTimeProvider(scenario.Now.AddMinutes(1));
        var secondTime = barrier.CreateTimeProvider(scenario.Now.AddDays(1));
        await using var firstContext = fixture.Database.CreateDbContext();
        await using var secondContext = fixture.Database.CreateDbContext();
        var first = new ExerciseCatalogPublicationService(firstContext, scenario.Deployment, firstTime);
        var second = new ExerciseCatalogPublicationService(secondContext, scenario.Deployment, secondTime);

        var monitor = ReleaseBarrierWhenSerializedAsync(barrier, fixture.Database.ConnectionString);
        var attempts = await Task.WhenAll(
            CapturePublicationAsync(first, firstTime, ReviewerId, RightsReference),
            CapturePublicationAsync(second, secondTime, ReviewerId, RightsReference));
        await monitor;

        Assert.All(attempts, attempt => Assert.Null(attempt.Error));
        var winnerTime = Assert.Single(new[] { firstTime, secondTime }, time => time.WasRead);
        await using var verificationContext = fixture.Database.CreateDbContext();
        var images = await verificationContext.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(CatalogExerciseCount, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
            Assert.Equal(winnerTime.GetConfiguredUtcNow(), image.ReviewedAt);
            Assert.Equal(winnerTime.GetConfiguredUtcNow(), image.PublishedAt);
        });
        Assert.Equal(ConcurrentVerificationReadCount, scenario.Storage.GetCount);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
    }

    [Fact]
    public async Task Verification_reads_exact_180_private_objects_without_writes()
    {
        var scenario = await fixture.PrepareAsync();
        scenario.Storage.ResetCounters();

        await scenario.Deployment.VerifyExactAsync(CatalogPath);

        Assert.Equal(CatalogObjectCount, scenario.Storage.GetCount);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
        var expectedKeys = ExerciseManifest.Load(CatalogPath)
            .SelectMany(item => new[]
            {
                ExerciseCatalogSeeder.MasterKey(item.Id),
                ExerciseCatalogSeeder.ThumbnailKey(item.Id)
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedKeys, scenario.Storage.ReadKeys.Order(StringComparer.Ordinal));
        Assert.All(scenario.Storage.ReadPrefixes, prefix => Assert.Equal("system/", prefix));
    }

    [Fact]
    public async Task Publication_fails_closed_when_one_object_is_missing()
    {
        var scenario = await fixture.PrepareAsync();
        var first = ExerciseManifest.Load(CatalogPath)[0];
        scenario.Storage.Remove(ExerciseCatalogSeeder.MasterKey(first.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        await AssertAllDraftAsync(scenario.Database);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_fails_closed_for_one_byte_or_content_type_mismatch(bool contentTypeMismatch)
    {
        var scenario = await fixture.PrepareAsync();
        var first = ExerciseManifest.Load(CatalogPath)[0];
        var key = ExerciseCatalogSeeder.ThumbnailKey(first.Id);
        if (contentTypeMismatch)
            scenario.Storage.ChangeContentType(key, "image/jpeg");
        else
            scenario.Storage.CorruptOneByte(key);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        await AssertAllDraftAsync(scenario.Database);
        Assert.Equal(0, scenario.Storage.PutCountAfterReset);
        Assert.Equal(0, scenario.Storage.DeleteCountAfterReset);
    }

    [Fact]
    public async Task Publication_rejects_only_89_image_rows_before_any_state_change()
    {
        var scenario = await fixture.PrepareAsync();
        var firstId = await scenario.Database.ExerciseImages.Select(image => image.Id).FirstAsync();
        await scenario.Database.ExerciseImages.Where(image => image.Id == firstId).ExecuteDeleteAsync();
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        var remaining = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(CatalogExerciseCount - 1, remaining.Length);
        Assert.All(remaining, AssertDraft);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("master-key")]
    [InlineData("thumbnail-key")]
    [InlineData("source-reference")]
    public async Task Publication_rejects_unexpected_image_identity_metadata(string mutation)
    {
        var scenario = await fixture.PrepareAsync();
        var image = await scenario.Database.ExerciseImages.AsNoTracking().OrderBy(item => item.Id).FirstAsync();
        switch (mutation)
        {
            case "version":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.Version, 2));
                break;
            case "master-key":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.MasterObjectKey, "system/unexpected/master.png"));
                break;
            case "thumbnail-key":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.ThumbnailObjectKey, "system/unexpected/thumbnail.png"));
                break;
            case "source-reference":
                await scenario.Database.ExerciseImages.Where(item => item.Id == image.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.SourceReference, "unexpected-source"));
                break;
        }
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        Assert.All(await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync(), AssertDraft);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("body-part")]
    [InlineData("tracking-mode")]
    public async Task Publication_rejects_unexpected_definition_metadata(string mutation)
    {
        var scenario = await fixture.PrepareAsync();
        var definition = await scenario.Database.Exercises.AsNoTracking().OrderBy(item => item.Id).FirstAsync();
        switch (mutation)
        {
            case "name":
                await scenario.Database.Exercises.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(item => item.Name, "Unexpected Exercise")
                        .SetProperty(item => item.NormalizedName, "UNEXPECTED EXERCISE"));
                break;
            case "body-part":
                var differentBodyPart = definition.BodyPart == BodyPart.Arms ? BodyPart.Back : BodyPart.Arms;
                await scenario.Database.Exercises.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.BodyPart, differentBodyPart));
                break;
            case "tracking-mode":
                var differentMode = definition.TrackingMode == TrackingMode.Weighted
                    ? TrackingMode.Bodyweight
                    : TrackingMode.Weighted;
                await scenario.Database.Exercises.Where(item => item.Id == definition.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.TrackingMode, differentMode));
                break;
        }
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        Assert.All(await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync(), AssertDraft);
    }

    [Fact]
    public async Task Publication_rejects_a_reviewed_row_without_publishing_any_row()
    {
        var scenario = await fixture.PrepareAsync();
        var first = await scenario.Database.ExerciseImages.OrderBy(image => image.Id).FirstAsync();
        first.Review(ReviewerId, RightsReference, true, true, true, scenario.Now);
        await scenario.Database.SaveChangesAsync();
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        var images = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Single(images, image => image.ReviewState == ExerciseImageReviewState.Reviewed);
        Assert.DoesNotContain(images, image => image.ReviewState == ExerciseImageReviewState.Published);
    }

    [Fact]
    public async Task Publication_preflights_all_created_timestamps_before_mutating_tracked_drafts()
    {
        var scenario = await fixture.PrepareAsync();
        var lastId = await scenario.Database.ExerciseImages.AsNoTracking()
            .OrderBy(image => image.ExerciseDefinitionId)
            .Select(image => image.Id)
            .LastAsync();
        await scenario.Database.ExerciseImages.Where(image => image.Id == lastId)
            .ExecuteUpdateAsync(update => update.SetProperty(image => image.CreatedAt, scenario.Now.AddDays(1)));
        scenario.Database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference));

        Assert.All(scenario.Database.ChangeTracker.Entries<ExerciseImage>(), entry => AssertDraft(entry.Entity));
        await AssertAllDraftAsync(scenario.Database);
    }

    [Fact]
    public async Task Publication_preserves_48_existing_publications_and_publishes_only_42_new_drafts()
    {
        var scenario = await fixture.PrepareAsync();
        var manifest = ExerciseManifest.Load(CatalogPath);
        var expansionIds = manifest
            .Where(item => ExpansionExerciseNames.Contains(item.Name))
            .Select(item => item.Id)
            .ToHashSet();
        var originalIds = manifest
            .Where(item => !ExpansionExerciseNames.Contains(item.Name))
            .Select(item => item.Id)
            .ToHashSet();
        Assert.Equal(42, expansionIds.Count);
        Assert.Equal(48, originalIds.Count);

        var originalPublishedAt = scenario.Now;
        var originalImages = await scenario.Database.ExerciseImages
            .Where(image => originalIds.Contains(image.ExerciseDefinitionId))
            .ToArrayAsync();
        foreach (var image in originalImages)
        {
            image.Review(ReviewerId, RightsReference, true, true, true, originalPublishedAt);
            image.Publish(originalPublishedAt);
        }

        var owner = User.Create($"catalog-upgrade-{Guid.NewGuid():N}@example.com", "hash");
        var custom = ExerciseDefinition.CreateCustom(owner.Id, "Owner Cable Press", BodyPart.Chest, TrackingMode.Weighted);
        var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), scenario.Now.AddMinutes(-10));
        workout.AddExercise(Guid.NewGuid(), originalIds.First(), TrackingMode.Weighted, 0);
        await scenario.Database.Users.AddAsync(owner);
        await scenario.Database.Exercises.AddAsync(custom);
        await scenario.Database.WorkoutSessions.AddAsync(workout);
        await scenario.Database.SaveChangesAsync();
        scenario.Database.ChangeTracker.Clear();
        var customBefore = await scenario.Database.Exercises.AsNoTracking()
            .SingleAsync(exercise => exercise.Id == custom.Id);
        var workoutBefore = await scenario.Database.WorkoutSessions.AsNoTracking()
            .SingleAsync(item => item.Id == workout.Id);

        scenario.Time.Advance(TimeSpan.FromDays(1));
        const string expansionRightsReference = "trackz-expanded-catalog-2026-09-05";

        await scenario.Publication.PublishAsync(
            CatalogPath,
            OtherReviewerId,
            expansionRightsReference);

        var images = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(CatalogExerciseCount, images.Length);
        Assert.All(images.Where(image => originalIds.Contains(image.ExerciseDefinitionId)), image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
            Assert.Equal(originalPublishedAt, image.PublishedAt);
        });
        Assert.All(images.Where(image => expansionIds.Contains(image.ExerciseDefinitionId)), image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(OtherReviewerId, image.ReviewedByUserId);
            Assert.Equal(expansionRightsReference, image.RightsReference);
            Assert.Equal(scenario.Now, image.PublishedAt);
        });

        var customAfter = await scenario.Database.Exercises.AsNoTracking()
            .SingleAsync(exercise => exercise.Id == custom.Id);
        var workoutAfter = await scenario.Database.WorkoutSessions.AsNoTracking()
            .SingleAsync(item => item.Id == workout.Id);
        Assert.Equal(customBefore.Name, customAfter.Name);
        Assert.Equal(customBefore.OwnerId, customAfter.OwnerId);
        Assert.Equal(customBefore.IsArchived, customAfter.IsArchived);
        Assert.Equal(workoutBefore.Status, workoutAfter.Status);
        Assert.Equal(workoutBefore.StartedAt, workoutAfter.StartedAt);
        Assert.Equal(workoutBefore.Version, workoutAfter.Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Published_rerun_preserves_existing_metadata_when_new_arguments_differ(bool conflictReviewer)
    {
        var scenario = await fixture.PrepareAsync();
        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);
        scenario.Database.ChangeTracker.Clear();

        await scenario.Publication.PublishAsync(
            CatalogPath,
            conflictReviewer ? OtherReviewerId : ReviewerId,
            conflictReviewer ? RightsReference : "different-rights-reference");

        var images = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.All(images, image =>
        {
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
            Assert.Equal(scenario.Now, image.ReviewedAt);
            Assert.Equal(scenario.Now, image.PublishedAt);
        });
    }

    [Fact]
    public async Task Exact_published_rerun_is_idempotent_with_unchanged_timestamps()
    {
        var scenario = await fixture.PrepareAsync();
        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, $"  {RightsReference}  ");
        scenario.Database.ChangeTracker.Clear();
        var before = await scenario.Database.ExerciseImages.AsNoTracking()
            .ToDictionaryAsync(image => image.Id, image => new { image.ReviewedAt, image.PublishedAt });
        scenario.Time.Advance(TimeSpan.FromDays(2));

        await scenario.Publication.PublishAsync(CatalogPath, ReviewerId, RightsReference);

        var after = await scenario.Database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.All(after, image =>
        {
            Assert.Equal(before[image.Id].ReviewedAt, image.ReviewedAt);
            Assert.Equal(before[image.Id].PublishedAt, image.PublishedAt);
            Assert.Equal(RightsReference, image.RightsReference);
        });
    }

    [Fact]
    public async Task Trigger_failure_on_24th_publish_update_rolls_back_all_90_rows()
    {
        var scenario = await fixture.PrepareAsync();
        await InstallFailureTriggerAsync(scenario.Database);
        try
        {
            await using (var publicationContext = fixture.Database.CreateDbContext())
            {
                var publication = new ExerciseCatalogPublicationService(
                    publicationContext,
                    scenario.Deployment,
                    scenario.Time);

                var error = await Assert.ThrowsAsync<DbUpdateException>(() => publication.PublishAsync(
                    CatalogPath,
                    ReviewerId,
                    RightsReference));
                Assert.Contains("injected catalog publication failure on update 24", error.ToString(), StringComparison.Ordinal);
            }

            await using var verificationContext = fixture.Database.CreateDbContext();
            var images = await verificationContext.ExerciseImages.AsNoTracking().ToArrayAsync();
            Assert.Equal(CatalogExerciseCount, images.Length);
            Assert.All(images, AssertDraft);
        }
        finally
        {
            await DropFailureTriggerAsync(scenario.Database);
        }

        await using var retryContext = fixture.Database.CreateDbContext();
        await new ExerciseCatalogPublicationService(retryContext, scenario.Deployment, scenario.Time)
            .PublishAsync(CatalogPath, ReviewerId, RightsReference);
        Assert.All(
            await retryContext.ExerciseImages.AsNoTracking().ToArrayAsync(),
            image => Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState));
    }

    [Fact]
    public async Task Cancellation_releases_publication_lock_and_rolls_back_for_a_clean_retry()
    {
        var scenario = await fixture.PrepareAsync();
        await InstallSlowPublicationTriggerAsync(scenario.Database);
        try
        {
            await using (var cancelledContext = fixture.Database.CreateDbContext())
            using (var cancellation = new CancellationTokenSource())
            {
                var publication = new ExerciseCatalogPublicationService(
                    cancelledContext,
                    scenario.Deployment,
                    scenario.Time);
                var attempt = publication.PublishAsync(
                    CatalogPath,
                    ReviewerId,
                    RightsReference,
                    cancellation.Token);
                await WaitForGrantedAdvisoryLockAsync(fixture.Database.ConnectionString);
                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
            }
        }
        finally
        {
            await DropSlowPublicationTriggerAsync(scenario.Database);
        }

        await using var retryContext = fixture.Database.CreateDbContext();
        await new ExerciseCatalogPublicationService(retryContext, scenario.Deployment, scenario.Time)
            .PublishAsync(CatalogPath, ReviewerId, RightsReference);
        var images = await retryContext.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(CatalogExerciseCount, images.Length);
        Assert.All(images, image =>
        {
            Assert.Equal(ExerciseImageReviewState.Published, image.ReviewState);
            Assert.Equal(ReviewerId, image.ReviewedByUserId);
            Assert.Equal(RightsReference, image.RightsReference);
        });
    }

    private static async Task AssertAllDraftAsync(AppDbContext database)
    {
        database.ChangeTracker.Clear();
        var images = await database.ExerciseImages.AsNoTracking().ToArrayAsync();
        Assert.Equal(CatalogExerciseCount, images.Length);
        Assert.All(images, AssertDraft);
    }

    private static void AssertDraft(ExerciseImage image)
    {
        Assert.Equal(ExerciseImageReviewState.Draft, image.ReviewState);
        Assert.Null(image.ReviewedByUserId);
        Assert.Null(image.ReviewedAt);
        Assert.Null(image.PublishedAt);
        Assert.Null(image.RightsReference);
        Assert.False(image.AnatomyApproved);
        Assert.False(image.MovementApproved);
        Assert.False(image.RightsApproved);
        Assert.False(image.IsReadyForUse);
    }

    private static async Task<PublicationAttempt> CapturePublicationAsync(
        ExerciseCatalogPublicationService publication,
        CoordinatedTimeProvider timeProvider,
        Guid reviewerId,
        string rightsReference)
    {
        try
        {
            await publication.PublishAsync(CatalogPath, reviewerId, rightsReference);
            return new PublicationAttempt(
                reviewerId,
                rightsReference,
                timeProvider,
                null);
        }
        catch (Exception error)
        {
            return new PublicationAttempt(
                reviewerId,
                rightsReference,
                timeProvider,
                error);
        }
    }

    private static async Task ReleaseBarrierWhenSerializedAsync(
        ConcurrentPublicationBarrier barrier,
        string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var monitor = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options);
        while (!barrier.IsReleased)
        {
            var waitingLocks = await monitor.Database
                .SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted")
                .SingleAsync(timeout.Token);
            if (waitingLocks > 0)
            {
                barrier.ReleaseSerializedWinner();
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task InstallFailureTriggerAsync(AppDbContext database)
    {
        await DropFailureTriggerAsync(database);
        await database.Database.ExecuteSqlRawAsync(
            "CREATE TABLE catalog_publication_trigger_counter (value integer NOT NULL)");
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO catalog_publication_trigger_counter (value) VALUES (0)");
        await database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_catalog_publication_on_24th_update() RETURNS trigger AS $$
            DECLARE update_count integer;
            BEGIN
                UPDATE catalog_publication_trigger_counter
                SET value = value + 1
                RETURNING value INTO update_count;
                IF update_count = 24 THEN
                    RAISE EXCEPTION 'injected catalog publication failure on update 24';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql
            """);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_catalog_publication_on_24th_update
            BEFORE UPDATE OF "ReviewState" ON exercise_images
            FOR EACH ROW
            WHEN (NEW."ReviewState" = 3 AND OLD."ReviewState" IS DISTINCT FROM NEW."ReviewState")
            EXECUTE FUNCTION fail_catalog_publication_on_24th_update()
            """);
    }

    private static async Task DropFailureTriggerAsync(AppDbContext database)
    {
        database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS fail_catalog_publication_on_24th_update ON exercise_images");
        await database.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS fail_catalog_publication_on_24th_update()");
        await database.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS catalog_publication_trigger_counter");
    }

    private static async Task InstallSlowPublicationTriggerAsync(AppDbContext database)
    {
        await DropSlowPublicationTriggerAsync(database);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION slow_catalog_publication_update() RETURNS trigger AS $$
            BEGIN
                PERFORM pg_sleep(60);
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql
            """);
        await database.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER slow_catalog_publication_update
            BEFORE UPDATE OF "ReviewState" ON exercise_images
            FOR EACH ROW
            WHEN (NEW."ReviewState" = 3 AND OLD."ReviewState" IS DISTINCT FROM NEW."ReviewState")
            EXECUTE FUNCTION slow_catalog_publication_update()
            """);
    }

    private static async Task DropSlowPublicationTriggerAsync(AppDbContext database)
    {
        database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS slow_catalog_publication_update ON exercise_images");
        await database.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS slow_catalog_publication_update()");
    }

    private static async Task WaitForGrantedAdvisoryLockAsync(string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var monitor = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options);
        while (true)
        {
            var grantedLocks = await monitor.Database
                .SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND granted")
                .SingleAsync(timeout.Token);
            if (grantedLocks > 0) return;
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static string CatalogPath => ExerciseCatalogPublicationFixture.CatalogPath;
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExerciseCatalogPublicationCollection : ICollectionFixture<ExerciseCatalogPublicationFixture>
{
    public const string Name = "Exercise catalog publication";
}

public sealed class ExerciseCatalogPublicationFixture : IAsyncLifetime
{
    private PostgreSqlFixture? _database;

    internal PostgreSqlFixture Database => _database
        ?? throw new InvalidOperationException("The PostgreSQL fixture has not started.");

    public async Task InitializeAsync() => _database = await PostgreSqlFixture.StartAsync();

    public async Task DisposeAsync()
    {
        if (_database is not null) await _database.DisposeAsync();
    }

    internal async Task<PublicationScenario> PrepareAsync()
    {
        var database = Database.Db;
        database.ChangeTracker.Clear();
        await database.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS fail_catalog_publication_on_24th_update ON exercise_images");
        await database.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS fail_catalog_publication_on_24th_update()");
        await database.Database.ExecuteSqlRawAsync(
            "DROP TABLE IF EXISTS catalog_publication_trigger_counter");
        await database.Database.ExecuteSqlRawAsync(
            "DROP TRIGGER IF EXISTS slow_catalog_publication_update ON exercise_images");
        await database.Database.ExecuteSqlRawAsync(
            "DROP FUNCTION IF EXISTS slow_catalog_publication_update()");
        await database.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE exercise_images, exercise_definitions CASCADE");

        var storage = new InspectableObjectStorage();
        var deployment = new ObjectStorageExerciseCatalogAssetDeployment(storage);
        var deployAndSeed = new ExerciseCatalogDeploymentService(
            deployment,
            new ExerciseCatalogSeeder(database, deployment));
        await deployAndSeed.DeployAndSeedAsync(CatalogPath);
        storage.ResetCounters();
        database.ChangeTracker.Clear();
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        var publication = new ExerciseCatalogPublicationService(database, deployment, time);
        return new PublicationScenario(database, storage, deployment, publication, time);
    }

    internal static string CatalogPath => Path.Combine(RepositoryRoot, "assets", "exercises", "catalog.json");

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the TrackZ repository root.");
        }
    }
}

internal sealed record PublicationScenario(
    AppDbContext Database,
    InspectableObjectStorage Storage,
    ObjectStorageExerciseCatalogAssetDeployment Deployment,
    ExerciseCatalogPublicationService Publication,
    MutableTimeProvider Time)
{
    public DateTimeOffset Now => Time.GetUtcNow();
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

internal sealed class ConcurrentPublicationBarrier
{
    private readonly ManualResetEventSlim _release = new(initialState: false);
    private int _arrivals;

    public bool IsReleased => _release.IsSet;

    public CoordinatedTimeProvider CreateTimeProvider(DateTimeOffset utcNow) => new(this, utcNow);

    public void ArriveAndWait()
    {
        if (Interlocked.Increment(ref _arrivals) == 2) _release.Set();
        if (!_release.Wait(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("Concurrent publication preflights did not reach a deterministic release condition.");
    }

    public void ReleaseSerializedWinner() => _release.Set();
}

internal sealed class CoordinatedTimeProvider(
    ConcurrentPublicationBarrier barrier,
    DateTimeOffset utcNow) : TimeProvider
{
    private readonly DateTimeOffset _utcNow = utcNow.ToUniversalTime();
    private int _wasRead;

    public bool WasRead => Volatile.Read(ref _wasRead) == 1;

    public DateTimeOffset GetConfiguredUtcNow() => _utcNow;

    public override DateTimeOffset GetUtcNow()
    {
        Interlocked.Exchange(ref _wasRead, 1);
        barrier.ArriveAndWait();
        return _utcNow;
    }
}

internal sealed record PublicationAttempt(
    Guid ReviewerId,
    string RightsReference,
    CoordinatedTimeProvider TimeProvider,
    Exception? Error);

internal sealed class InspectableObjectStorage : IObjectStorage
{
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _readKeys = new();
    private readonly ConcurrentQueue<string> _readPrefixes = new();
    private int _getCount;
    private int _putCount;
    private int _deleteCount;
    private int _putCountAtReset;
    private int _deleteCountAtReset;

    public int GetCount => Volatile.Read(ref _getCount);
    public int PutCount => Volatile.Read(ref _putCount);
    public int DeleteCount => Volatile.Read(ref _deleteCount);
    public int PutCountAfterReset => PutCount - _putCountAtReset;
    public int DeleteCountAfterReset => DeleteCount - _deleteCountAtReset;
    public IEnumerable<string> ReadKeys => _readKeys;
    public IEnumerable<string> ReadPrefixes => _readPrefixes;

    public Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _getCount);
        _readKeys.Enqueue(key);
        _readPrefixes.Enqueue(ownerPrefix);
        return Task.FromResult(_objects.TryGetValue(key, out var value)
            ? new ObjectStorageObject(
                value.Bytes.LongLength,
                value.ContentType,
                new MemoryStream(value.Bytes, writable: false))
            : null);
    }

    public async Task PutAsync(
        string ownerPrefix,
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _putCount);
        await using var output = new MemoryStream();
        await content.CopyToAsync(output, cancellationToken);
        _objects[key] = new StoredObject(output.ToArray(), contentType);
    }

    public Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deleteCount);
        _objects.Remove(key);
        return Task.CompletedTask;
    }

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _getCount, 0);
        _putCountAtReset = PutCount;
        _deleteCountAtReset = DeleteCount;
        _readKeys.Clear();
        _readPrefixes.Clear();
    }

    public void Remove(string key) => _objects.Remove(key);

    public void CorruptOneByte(string key)
    {
        var value = _objects[key];
        var corrupted = value.Bytes.ToArray();
        corrupted[corrupted.Length / 2] ^= 0xFF;
        _objects[key] = value with { Bytes = corrupted };
    }

    public void ChangeContentType(string key, string contentType) =>
        _objects[key] = _objects[key] with { ContentType = contentType };

    private sealed record StoredObject(byte[] Bytes, string ContentType);
}
