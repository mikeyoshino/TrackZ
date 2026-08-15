using Microsoft.EntityFrameworkCore;
using TrackZ.Domain.Exercises;

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
        Assert.Contains(await database.Db.Database.GetAppliedMigrationsAsync(), migration => migration.EndsWith("AddExerciseCatalog", StringComparison.Ordinal));
    }
}
