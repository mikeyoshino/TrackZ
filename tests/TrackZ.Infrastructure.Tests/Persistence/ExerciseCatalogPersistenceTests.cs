using Microsoft.EntityFrameworkCore;
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
    }
}
