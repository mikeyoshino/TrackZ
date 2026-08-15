using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class ExerciseCatalogPersistenceTests
{
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

    [Fact]
    public async Task Catalog_migration_matches_the_current_model()
    {
        await using var database = await PostgreSqlFixture.StartAsync();

        Assert.Empty(await database.Db.Database.GetPendingMigrationsAsync());
        Assert.False(database.Db.Database.HasPendingModelChanges());
        Assert.Contains(await database.Db.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("AddExerciseCatalog", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Custom_exercise_migration_designer_contains_the_complete_target_model()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(
            migrations.Migrations["20260815051856_AddImageUploadTicketRelationships"],
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
    }

    [Fact]
    public async Task Custom_exercise_migration_target_matches_current_snapshot_relational_metadata()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(
            migrations.Migrations["20260815051856_AddImageUploadTicketRelationships"],
            database.Db.Database.ProviderName!)!;

        Assert.Equal(
            DescribeRelationalModel(migrations.ModelSnapshot!.Model),
            DescribeRelationalModel(migration.TargetModel));
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
