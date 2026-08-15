using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Media;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class ExerciseCatalogPersistenceTests
{
    [Fact]
    public async Task Accepted_upload_attempt_remains_durable_through_monotonic_successor_states_and_requires_the_exact_contract()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var exercises = new[]
        {
            ExerciseDefinition.CreateCustom(ownerId, "Accepted Uploaded", BodyPart.Chest, TrackingMode.Weighted),
            ExerciseDefinition.CreateCustom(ownerId, "Accepted Processing", BodyPart.Chest, TrackingMode.Weighted),
            ExerciseDefinition.CreateCustom(ownerId, "Accepted Completed", BodyPart.Chest, TrackingMode.Weighted),
            ExerciseDefinition.CreateCustom(ownerId, "Not Accepted Pending", BodyPart.Chest, TrackingMode.Weighted),
            ExerciseDefinition.CreateCustom(ownerId, "Not Accepted Uploading", BodyPart.Chest, TrackingMode.Weighted)
        };
        var tickets = exercises.Select(exercise => ImageUploadTicket.Create(
            ownerId,
            exercise.Id,
            $"staging/{ownerId:D}/{exercise.Id:D}/reserved",
            "image/png",
            4,
            now.AddMinutes(5))).ToArray();
        await database.Db.Exercises.AddRangeAsync(exercises);
        foreach (var ticket in tickets) await database.Db.AddTicketAsync(ticket, default);
        await database.Db.SaveAsync(default);

        var acceptedKeys = new string[3];
        await using (var transitions = database.CreateDbContext())
        {
            for (var index = 0; index < acceptedKeys.Length; index++)
            {
                var claim = await transitions.TryClaimUploadAsync(
                    tickets[index].Id, ownerId, TimeSpan.FromMinutes(2), default);
                acceptedKeys[index] = claim.StagingObjectKey;
                Assert.Equal(
                    StagingUploadTransition.Uploaded,
                    await transitions.TryMarkUploadedAsync(
                        tickets[index].Id, ownerId, claim.UploadLeaseId, default));
            }

            _ = await transitions.TryClaimUploadAsync(
                tickets[4].Id, ownerId, TimeSpan.FromMinutes(2), default);
        }

        Guid processingLeaseId;
        await using (var processing = database.CreateDbContext())
        {
            var ticket = (await processing.FindOwnedTicketAsync(tickets[1].Id, ownerId, default))!;
            Assert.True(ticket.TryClaim(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
            processingLeaseId = ticket.ProcessingLeaseId!.Value;
            await processing.SaveAsync(default);
        }

        await using (var completed = database.CreateDbContext())
        {
            var ticket = (await completed.FindOwnedTicketAsync(tickets[2].Id, ownerId, default))!;
            Assert.True(ticket.TryClaim(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
            await completed.SaveAsync(default);
            await completed.CommitCompletionAsync(
                ticket.Id,
                ownerId,
                ticket.ProcessingLeaseId!.Value,
                $"private/{ownerId:D}/accepted/master.jpg",
                $"private/{ownerId:D}/accepted/thumbnail.jpg",
                default);
        }

        await using var verify = database.CreateDbContext();
        for (var index = 0; index < acceptedKeys.Length; index++)
        {
            Assert.True(await verify.IsAcceptedUploadAttemptDurableAsync(
                tickets[index].Id, ownerId, acceptedKeys[index], "image/png", 4, default));
        }

        Assert.False(await verify.IsAcceptedUploadAttemptDurableAsync(
            tickets[0].Id, otherOwnerId, acceptedKeys[0], "image/png", 4, default));
        Assert.False(await verify.IsAcceptedUploadAttemptDurableAsync(
            tickets[0].Id, ownerId, acceptedKeys[0] + "-different", "image/png", 4, default));
        Assert.False(await verify.IsAcceptedUploadAttemptDurableAsync(
            tickets[0].Id, ownerId, acceptedKeys[0], "image/jpeg", 4, default));
        Assert.False(await verify.IsAcceptedUploadAttemptDurableAsync(
            tickets[0].Id, ownerId, acceptedKeys[0], "image/png", 5, default));
        Assert.False(await verify.IsAcceptedUploadAttemptDurableAsync(
            tickets[3].Id, ownerId, tickets[3].StagingObjectKey, "image/png", 4, default));
        Assert.False(await verify.IsAcceptedUploadAttemptDurableAsync(
            tickets[4].Id, ownerId,
            (await verify.FindOwnedTicketAsync(tickets[4].Id, ownerId, default))!.StagingObjectKey,
            "image/png", 4, default));

        Assert.NotEqual(Guid.Empty, processingLeaseId);
    }

    [Fact]
    public async Task Three_expired_upload_claim_crashes_are_each_cleaned_before_the_next_reclaim()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var storage = new RecordingObjectStorage();
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "Crash Loop Press", BodyPart.Chest, TrackingMode.Weighted);
        var ticket = ImageUploadTicket.Create(
            ownerId,
            exercise.Id,
            $"staging/{ownerId:D}/initial",
            "image/jpeg",
            4,
            DateTimeOffset.UtcNow.AddMinutes(5));
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.AddTicketAsync(ticket, default);
        await database.Db.SaveAsync(default);

        var crashedKeys = new List<string>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            UploadClaim claim;
            await using (var claiming = database.CreateDbContext())
                claim = await claiming.TryClaimUploadAsync(ticket.Id, ownerId, TimeSpan.Zero, default);
            crashedKeys.Add(claim.StagingObjectKey);
            await storage.PutAsync($"staging/{ownerId:D}/", claim.StagingObjectKey, new MemoryStream([1, 2, 3, 4]), "image/jpeg", default);

            await using (var recovering = database.CreateDbContext())
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    recovering.TryClaimUploadAsync(ticket.Id, ownerId, TimeSpan.FromMinutes(1), default));
            }

            await using (var cleanup = database.CreateDbContext())
            {
                var candidate = Assert.Single(await cleanup.ListCleanupCandidatesAsync(DateTimeOffset.UtcNow, default));
                Assert.Equal(claim.StagingObjectKey, candidate.StagingKey);
                var cleanupClaim = await cleanup.TryClaimCleanupAsync(candidate, DateTimeOffset.UtcNow, default);
                Assert.NotNull(cleanupClaim);
                await storage.DeleteAsync($"staging/{ownerId:D}/", cleanupClaim!.StagingKey!, default);
                Assert.True(await cleanup.CompleteCleanupClaimAsync(ticket.Id, cleanupClaim.CleanupClaimId!.Value, default));
            }
        }

        await using var final = database.CreateDbContext();
        var next = await final.TryClaimUploadAsync(ticket.Id, ownerId, TimeSpan.FromMinutes(1), default);
        Assert.DoesNotContain(next.StagingObjectKey, crashedKeys);
        Assert.Empty(storage.Keys);
        Assert.Equal(3, storage.DeletedKeys.Count);
        Assert.Equal(crashedKeys, storage.DeletedKeys);
    }

    [Fact]
    public async Task Custom_image_keeps_nullable_review_state_and_does_not_persist_derived_readiness()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "Private Press", BodyPart.Chest, TrackingMode.Weighted);
        var image = ExerciseImage.CreateCustomUpload(exercise, ownerId, "masters/private-press", "thumbs/private-press", 1, "camera-roll");
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.ExerciseImages.AddAsync(image);
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var persisted = await database.Db.ExerciseImages.SingleAsync();
        var entity = database.Db.Model.FindEntityType(typeof(ExerciseImage));

        Assert.Null(persisted.ReviewState);
        Assert.True(persisted.IsReadyForUse);
        Assert.True(entity!.FindProperty(nameof(ExerciseImage.ReviewState))!.IsNullable);
        Assert.Null(entity.FindProperty(nameof(ExerciseImage.IsReadyForUse)));
    }

    private sealed class RecordingObjectStorage : IObjectStorage
    {
        public HashSet<string> Keys { get; } = [];
        public List<string> DeletedKeys { get; } = [];

        public Task<ObjectStorageObject?> GetAsync(string ownerPrefix, string key, CancellationToken cancellationToken) =>
            Task.FromResult<ObjectStorageObject?>(Keys.Contains(key)
                ? new ObjectStorageObject(4, "image/jpeg", new MemoryStream([1, 2, 3, 4]))
                : null);

        public Task PutAsync(string ownerPrefix, string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            Assert.StartsWith(ownerPrefix, key, StringComparison.Ordinal);
            Keys.Add(key);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string ownerPrefix, string key, CancellationToken cancellationToken)
        {
            Assert.StartsWith(ownerPrefix, key, StringComparison.Ordinal);
            Assert.True(Keys.Remove(key));
            DeletedKeys.Add(key);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Catalog_migration_matches_the_current_model()
    {
        await using var database = await PostgreSqlFixture.StartAsync();

        Assert.Empty(await database.Db.Database.GetPendingMigrationsAsync());
        Assert.False(database.Db.Database.HasPendingModelChanges());
        Assert.Contains(await database.Db.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("AddExerciseCatalog", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Latest_custom_exercise_migration_designer_contains_the_complete_target_model()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(
            migrations.Migrations["20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage"],
            database.Db.Database.ProviderName!)!;

        var entityNames = migration.TargetModel.GetEntityTypes()
            .Select(entity => entity.Name[(entity.Name.LastIndexOf('.') + 1)..])
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["ExerciseDefinition", "ExerciseImage", "ExercisePerformance", "ImageUploadTicket", "RefreshToken", "User"], entityNames);
        var exercise = migration.TargetModel.FindEntityType(typeof(ExerciseDefinition).FullName!)!;
        var performance = migration.TargetModel.FindEntityType(typeof(ExercisePerformance).FullName!)!;
        var uniqueness = Assert.Single(exercise.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(ExerciseDefinition.OwnerId), nameof(ExerciseDefinition.NormalizedName)]));
        var relationship = Assert.Single(performance.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType == exercise);

        Assert.True(uniqueness.IsUnique);
        Assert.Equal("\"OwnerId\" IS NOT NULL AND NOT \"IsArchived\"", uniqueness.GetFilter());
        Assert.Equal([nameof(ExercisePerformance.ExerciseDefinitionId)], relationship.Properties.Select(property => property.Name));
        Assert.Equal([nameof(ExerciseDefinition.Id)], relationship.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Contains(performance.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(ExercisePerformance.ExerciseDefinitionId)]));
        Assert.DoesNotContain(exercise.GetKeys(), key => key.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(ExerciseDefinition.Id), nameof(ExerciseDefinition.TrackingMode)]));
        Assert.NotNull(exercise.FindProperty(nameof(ExerciseDefinition.ClientOperationId)));
        Assert.NotNull(exercise.FindProperty(nameof(ExerciseDefinition.LibraryImageId)));
        var idempotency = Assert.Single(exercise.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(ExerciseDefinition.OwnerId), nameof(ExerciseDefinition.ClientOperationId)]));
        Assert.True(idempotency.IsUnique);
        Assert.Equal("\"OwnerId\" IS NOT NULL AND \"ClientOperationId\" IS NOT NULL", idempotency.GetFilter());
        Assert.Contains(exercise.GetForeignKeys(), foreignKey => foreignKey.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(ExerciseDefinition.LibraryImageId)]));
    }

    [Fact]
    public async Task Latest_custom_exercise_migration_target_matches_current_snapshot_relational_metadata()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(
            migrations.Migrations["20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage"],
            database.Db.Database.ProviderName!)!;

        Assert.Equal(
            DescribeRelationalModel(migrations.ModelSnapshot!.Model),
            DescribeRelationalModel(migration.TargetModel));
    }

    [Fact]
    public async Task Custom_exercise_migration_is_chronologically_after_upload_tickets_without_rewriting_history()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var ids = migrations.Migrations.Keys.ToArray();
        var customIndex = Array.IndexOf(ids, "20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage");
        var uploadIndex = Array.IndexOf(ids, "20260815130000_AddImageUploadTickets");
        var historical = migrations.CreateMigration(
            migrations.Migrations["20260815130000_AddImageUploadTickets"],
            database.Db.Database.ProviderName!)!;
        var latest = migrations.CreateMigration(
            migrations.Migrations["20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage"],
            database.Db.Database.ProviderName!)!;
        var historicalExercise = historical.TargetModel.FindEntityType(typeof(ExerciseDefinition).FullName!)!;

        Assert.True(customIndex > uploadIndex);
        Assert.Null(historicalExercise.FindProperty(nameof(ExerciseDefinition.ClientOperationId)));
        Assert.Null(historicalExercise.FindProperty(nameof(ExerciseDefinition.LibraryImageId)));
        Assert.Equal(2, latest.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>()
            .Count(operation => operation.Table == "exercise_definitions"));
        Assert.Equal(2, latest.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation>()
            .Count(operation => operation.Table == "exercise_definitions"));
    }

    [Fact]
    public async Task Upload_ticket_migration_models_uploaded_state_and_fenced_processing_lease()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(
            migrations.Migrations["20260815130000_AddImageUploadTickets"],
            database.Db.Database.ProviderName!)!;
        var ticket = migration.TargetModel.FindEntityType(typeof(ImageUploadTicket))!;

        Assert.NotNull(ticket.FindProperty(nameof(ImageUploadTicket.ProcessingLeaseId)));
        Assert.Contains(ticket.GetCheckConstraints(), check => check.Name == "CK_image_upload_tickets_state" && check.Sql.Contains("1, 2, 3, 4, 5", StringComparison.Ordinal));
        Assert.Contains(ticket.GetCheckConstraints(), check => check.Name == "CK_image_upload_tickets_processing_lease" && check.Sql.Contains("ProcessingLeaseId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stale_processing_lease_cannot_commit_after_expiry_cleanup_claim_terminalizes_it()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "Lease Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = DateTimeOffset.UtcNow;
        var ticket = ImageUploadTicket.Create(ownerId, exercise.Id, "staging/lease", "image/jpeg", 10, now.AddMinutes(5));
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.AddTicketAsync(ticket, default);
        await database.Db.SaveAsync(default);

        await using (var uploaded = database.CreateDbContext())
        {
            Assert.Equal(StagingUploadTransition.Uploaded, await uploaded.TryMarkUploadedAsync(ticket.Id, ownerId, default));
        }

        Guid firstLease;
        await using (var first = database.CreateDbContext())
        {
            var firstTicket = (await first.FindOwnedTicketAsync(ticket.Id, ownerId, default))!;
            Assert.True(firstTicket.TryClaim(now, TimeSpan.FromMinutes(1)));
            firstLease = firstTicket.ProcessingLeaseId!.Value;
            await first.SaveAsync(default);
        }

        await using (var cleanup = database.CreateDbContext())
        {
            var expiredTicket = (await cleanup.FindOwnedTicketAsync(ticket.Id, ownerId, default))!;
            Assert.False(expiredTicket.TryClaim(now.AddMinutes(2), TimeSpan.FromMinutes(1)));
            await cleanup.SaveAsync(default);
            var candidate = Assert.Single(await cleanup.ListCleanupCandidatesAsync(now.AddMinutes(2), default));
            var claim = await cleanup.TryClaimCleanupAsync(candidate, now.AddMinutes(2), default);
            Assert.NotNull(claim);
            Assert.True(await cleanup.CompleteCleanupClaimAsync(ticket.Id, claim!.CleanupClaimId!.Value, default));
        }

        await using (var stale = database.CreateDbContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => stale.CommitCompletionAsync(ticket.Id, ownerId, firstLease, "private/old/master.jpg", "private/old/thumbnail.jpg", default));
        }
        await using var verify = database.CreateDbContext();
        var persisted = await verify.ImageUploadTickets.SingleAsync();
        Assert.Equal(ImageUploadState.Expired, persisted.State);
        Assert.Empty(await verify.ExerciseImages.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_put_state_transitions_have_one_uploaded_winner()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "PUT Race Press", BodyPart.Chest, TrackingMode.Weighted);
        var ticket = ImageUploadTicket.Create(ownerId, exercise.Id, "staging/race", "image/jpeg", 10, DateTimeOffset.UtcNow.AddMinutes(5));
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.AddTicketAsync(ticket, default);
        await database.Db.SaveAsync(default);

        await using var left = database.CreateDbContext();
        await using var right = database.CreateDbContext();
        // Match the gateway's read-before-write shape so the transition must not trust EF's
        // per-context tracked Pending instance once the competing request commits.
        Assert.NotNull(await left.FindOwnedTicketAsync(ticket.Id, ownerId, default));
        Assert.NotNull(await right.FindOwnedTicketAsync(ticket.Id, ownerId, default));
        var outcomes = await Task.WhenAll(
            left.TryMarkUploadedAsync(ticket.Id, ownerId, default),
            right.TryMarkUploadedAsync(ticket.Id, ownerId, default));

        Assert.Equal(1, outcomes.Count(result => result == StagingUploadTransition.Uploaded));
        Assert.Equal(1, outcomes.Count(result => result == StagingUploadTransition.RetainedByAnotherUpload));
    }

    [Fact]
    public async Task Concurrent_tickets_for_one_exercise_receive_unique_serialized_versions()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "Concurrent Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = DateTimeOffset.UtcNow;
        var first = ImageUploadTicket.Create(ownerId, exercise.Id, "staging/first", "image/jpeg", 10, now.AddMinutes(5));
        var second = ImageUploadTicket.Create(ownerId, exercise.Id, "staging/second", "image/jpeg", 10, now.AddMinutes(5));
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.AddTicketAsync(first, default);
        await database.Db.AddTicketAsync(second, default);
        await database.Db.SaveAsync(default);

        Guid firstLease;
        Guid secondLease;
        await using (var uploads = database.CreateDbContext())
        {
            Assert.Equal(StagingUploadTransition.Uploaded, await uploads.TryMarkUploadedAsync(first.Id, ownerId, default));
            Assert.Equal(StagingUploadTransition.Uploaded, await uploads.TryMarkUploadedAsync(second.Id, ownerId, default));
        }
        await using (var claims = database.CreateDbContext())
        {
            var firstClaim = (await claims.FindOwnedTicketAsync(first.Id, ownerId, default))!;
            var secondClaim = (await claims.FindOwnedTicketAsync(second.Id, ownerId, default))!;
            Assert.True(firstClaim.TryClaim(now, TimeSpan.FromMinutes(2)));
            Assert.True(secondClaim.TryClaim(now, TimeSpan.FromMinutes(2)));
            firstLease = firstClaim.ProcessingLeaseId!.Value;
            secondLease = secondClaim.ProcessingLeaseId!.Value;
            await claims.SaveAsync(default);
        }

        await using var firstCommit = database.CreateDbContext();
        await using var secondCommit = database.CreateDbContext();
        await Task.WhenAll(
            firstCommit.CommitCompletionAsync(first.Id, ownerId, firstLease, "private/first/master.jpg", "private/first/thumb.jpg", default),
            secondCommit.CommitCompletionAsync(second.Id, ownerId, secondLease, "private/second/master.jpg", "private/second/thumb.jpg", default));

        await using var verify = database.CreateDbContext();
        var versions = await verify.ExerciseImages.OrderBy(image => image.Version).Select(image => image.Version).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, versions);
    }

    [Fact]
    public async Task Commit_rechecks_archival_and_preserves_the_prior_ready_image()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateCustom(ownerId, "Archive Press", BodyPart.Chest, TrackingMode.Weighted);
        var prior = ExerciseImage.CreateCustomUpload(exercise, ownerId, "private/prior/master.jpg", "private/prior/thumb.jpg", 1, "prior");
        var now = DateTimeOffset.UtcNow;
        var ticket = ImageUploadTicket.Create(ownerId, exercise.Id, "staging/archive", "image/jpeg", 10, now.AddMinutes(5));
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.ExerciseImages.AddAsync(prior);
        await database.Db.AddTicketAsync(ticket, default);
        await database.Db.SaveAsync(default);

        Guid lease;
        await using (var upload = database.CreateDbContext())
        {
            Assert.Equal(StagingUploadTransition.Uploaded, await upload.TryMarkUploadedAsync(ticket.Id, ownerId, default));
            var claim = (await upload.FindOwnedTicketAsync(ticket.Id, ownerId, default))!;
            Assert.True(claim.TryClaim(now, TimeSpan.FromMinutes(2)));
            lease = claim.ProcessingLeaseId!.Value;
            await upload.SaveAsync(default);
        }
        await using (var archive = database.CreateDbContext())
        {
            var active = (await archive.FindOwnedActiveExerciseAsync(exercise.Id, ownerId, default))!;
            active.Archive();
            await archive.SaveAsync(default);
        }
        await using (var commit = database.CreateDbContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => commit.CommitCompletionAsync(ticket.Id, ownerId, lease, "private/new/master.jpg", "private/new/thumb.jpg", default));
        }
        await using var verify = database.CreateDbContext();
        Assert.Equal("private/prior/master.jpg", (await verify.ExerciseImages.SingleAsync()).MasterObjectKey);
    }

    [Fact]
    public async Task Custom_exercise_migration_round_trip_preserves_valid_history_and_restores_trigger_integrity()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Migration Press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.ExercisePerformances.AddAsync(ExercisePerformance.Create(
            Guid.NewGuid(), exercise.Id, TrackingMode.Weighted, DateTimeOffset.UtcNow,
            new ExercisePerformanceSet(60m, null, 8), new ExercisePerformanceSet(70m, null, 5)));
        await database.Db.SaveChangesAsync();

        var migrator = database.Db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260815035525_AddExerciseCatalog");
        await migrator.MigrateAsync();

        database.Db.ChangeTracker.Clear();
        Assert.Equal(1, await database.Db.ExercisePerformances.CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE exercise_definitions SET \"TrackingMode\" = {(int)TrackingMode.Bodyweight} WHERE \"Id\" = {exercise.Id}"));
        Assert.False(database.Db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Active_custom_names_are_case_insensitively_unique_per_owner_but_can_be_reused_after_archive()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var ownerId = Guid.NewGuid();
        var original = ExerciseDefinition.CreateCustom(ownerId, "My Press", BodyPart.Chest, TrackingMode.Weighted);
        var differentOwner = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "my press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddRangeAsync(original, differentOwner);
        await database.Db.SaveChangesAsync();

        var duplicate = ExerciseDefinition.CreateCustom(ownerId, "MY PRESS", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Db.SaveChangesAsync());
        database.Db.Entry(duplicate).State = EntityState.Detached;

        original.Archive();
        await database.Db.SaveChangesAsync();
        var replacement = ExerciseDefinition.CreateCustom(ownerId, "my press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(replacement);
        await database.Db.SaveChangesAsync();

        Assert.Equal(3, await database.Db.Exercises.CountAsync());
        Assert.True((await database.Db.Exercises.FindAsync(original.Id))!.IsArchived);
    }

    [Fact]
    public async Task Performance_round_trips_valid_mode_shapes_and_database_rejects_invalid_weighted_shape()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var weightedExercise = ExerciseDefinition.CreateSystem("Weighted", BodyPart.Chest, TrackingMode.Weighted);
        var bodyweightExercise = ExerciseDefinition.CreateSystem("Bodyweight", BodyPart.Core, TrackingMode.Bodyweight);
        var assistedExercise = ExerciseDefinition.CreateSystem("Assisted", BodyPart.Back, TrackingMode.Assisted);
        await database.Db.Exercises.AddRangeAsync(weightedExercise, bodyweightExercise, assistedExercise);
        await database.Db.ExercisePerformances.AddRangeAsync(
            ExercisePerformance.Create(Guid.NewGuid(), weightedExercise.Id, TrackingMode.Weighted, DateTimeOffset.UtcNow, new ExercisePerformanceSet(70m, null, 8), new ExercisePerformanceSet(75m, null, 5)),
            ExercisePerformance.Create(Guid.NewGuid(), bodyweightExercise.Id, TrackingMode.Bodyweight, DateTimeOffset.UtcNow, new ExercisePerformanceSet(null, null, 12), new ExercisePerformanceSet(null, null, 15)),
            ExercisePerformance.Create(Guid.NewGuid(), assistedExercise.Id, TrackingMode.Assisted, DateTimeOffset.UtcNow, new ExercisePerformanceSet(null, 25m, 10), new ExercisePerformanceSet(null, 20m, 12)));
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        Assert.Equal(3, await database.Db.ExercisePerformances.CountAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO exercise_performances (\"Id\", \"UserId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"LastBestReps\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {weightedExercise.Id}, {1}, {8})"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO exercise_performances (\"Id\", \"UserId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"LastPerformedAt\", \"LastBestWeightKg\", \"LastBestReps\", \"AllTimeBestWeightKg\", \"AllTimeBestReps\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {weightedExercise.Id}, {99}, {DateTimeOffset.UtcNow}, {10m}, {1}, {10m}, {1})"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO exercise_performances (\"Id\", \"UserId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"LastPerformedAt\", \"LastBestAssistedKg\", \"LastBestReps\", \"AllTimeBestAssistedKg\", \"AllTimeBestReps\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {weightedExercise.Id}, {3}, {DateTimeOffset.UtcNow}, {10m}, {1}, {10m}, {1})"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO exercise_performances (\"Id\", \"UserId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"LastPerformedAt\", \"LastBestReps\", \"AllTimeBestReps\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {weightedExercise.Id}, {2}, {DateTimeOffset.UtcNow}, {8}, {10})"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_definitions SET \"TrackingMode\" = {2} WHERE \"Id\" = {weightedExercise.Id}"));
    }

    private static IReadOnlyList<string> DescribeRelationalModel(IReadOnlyModel model)
    {
        var modelPrefix = $"model|default-schema={model.GetDefaultSchema()}|{DescribeAnnotations(model)}";

        return new[] { modelPrefix }
            .Concat(model.GetEntityTypes()
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .SelectMany(entity =>
            {
                var entityPrefix = $"entity|{entity.Name}|{entity.GetSchema()}|{entity.GetTableName()}|{DescribeAnnotations(entity)}";
                var properties = entity.GetProperties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => $"property|{entity.Name}|{property.Name}|{property.ClrType.AssemblyQualifiedName}|{property.IsNullable}|{property.ValueGenerated}|{property.GetColumnType()}|{property.GetMaxLength()}|{property.GetPrecision()}|{property.GetScale()}|{DescribeAnnotations(property)}");
                var keys = entity.GetKeys()
                    .OrderBy(key => string.Join(',', key.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                    .Select(key => $"key|{entity.Name}|{key.IsPrimaryKey()}|{string.Join(',', key.Properties.Select(property => property.Name))}|{key.GetName()}|{DescribeAnnotations(key)}");
                var foreignKeys = entity.GetForeignKeys()
                    .OrderBy(foreignKey => string.Join(',', foreignKey.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                    .Select(foreignKey => $"foreign-key|{entity.Name}|{string.Join(',', foreignKey.Properties.Select(property => property.Name))}|{foreignKey.PrincipalEntityType.Name}|{string.Join(',', foreignKey.PrincipalKey.Properties.Select(property => property.Name))}|{foreignKey.DeleteBehavior}|{foreignKey.IsRequired}|{foreignKey.GetConstraintName()}|{DescribeAnnotations(foreignKey)}");
                var indexes = entity.GetIndexes()
                    .OrderBy(index => string.Join(',', index.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                    .Select(index => $"index|{entity.Name}|{string.Join(',', index.Properties.Select(property => property.Name))}|{index.IsUnique}|{index.GetDatabaseName()}|{index.GetFilter()}|{DescribeAnnotations(index)}");
                var checks = entity.GetCheckConstraints()
                    .OrderBy(check => check.Name, StringComparer.Ordinal)
                    .Select(check => $"check|{entity.Name}|{check.Name}|{check.Sql}|{DescribeAnnotations(check)}");

                return new[] { entityPrefix }
                    .Concat(properties)
                    .Concat(keys)
                    .Concat(foreignKeys)
                    .Concat(indexes)
                    .Concat(checks);
            }))
            .ToArray();
    }

    private static string DescribeAnnotations(IReadOnlyAnnotatable annotatable) => string.Join(
        ',',
        annotatable.GetAnnotations()
            .OrderBy(annotation => annotation.Name, StringComparer.Ordinal)
            .Select(annotation => $"{annotation.Name}={annotation.Value}"));
}
