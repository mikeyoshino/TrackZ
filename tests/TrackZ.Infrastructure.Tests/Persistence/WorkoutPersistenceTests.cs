using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Workouts;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class WorkoutPersistenceTests
{
    [Fact]
    public async Task Model_maps_private_collections_tombstones_concurrency_numeric_precision_and_integrity()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var model = database.Db.Model;
        var session = model.FindEntityType(typeof(WorkoutSession))!;
        var exercise = model.FindEntityType(typeof(WorkoutExercise))!;
        var set = model.FindEntityType(typeof(SetEntry))!;

        Assert.NotNull(session.FindNavigation("_exercises"));
        Assert.Null(session.FindProperty(nameof(WorkoutSession.Exercises)));
        Assert.Null(session.FindProperty(nameof(WorkoutSession.ExerciseEntries)));
        Assert.Null(session.FindProperty(nameof(WorkoutSession.IsDeleted)));
        Assert.True(session.FindProperty(nameof(WorkoutSession.Version))!.IsConcurrencyToken);
        Assert.NotNull(session.FindProperty(nameof(WorkoutSession.DeletedAt)));

        Assert.NotNull(exercise.FindNavigation("_sets"));
        Assert.Null(exercise.FindProperty(nameof(WorkoutExercise.Sets)));
        Assert.Null(exercise.FindProperty(nameof(WorkoutExercise.SetEntries)));
        Assert.Null(exercise.FindProperty(nameof(WorkoutExercise.IsDeleted)));
        Assert.Null(exercise.FindProperty("LastMutationAt"));
        Assert.True(exercise.FindProperty(nameof(WorkoutExercise.Version))!.IsConcurrencyToken);
        Assert.NotNull(exercise.FindProperty(nameof(WorkoutExercise.DeletedAt)));

        Assert.Null(set.FindProperty(nameof(SetEntry.Measurement)));
        Assert.Null(set.FindProperty(nameof(SetEntry.IsDeleted)));
        Assert.Null(set.FindProperty("LastMutationAt"));
        Assert.Equal("numeric(8,3)", set.FindProperty(nameof(SetEntry.WeightKg))!.GetColumnType());
        Assert.Equal("numeric(8,3)", set.FindProperty(nameof(SetEntry.AssistedKg))!.GetColumnType());
        Assert.True(set.FindProperty(nameof(SetEntry.Version))!.IsConcurrencyToken);
        Assert.NotNull(set.FindProperty(nameof(SetEntry.DeletedAt)));

        Assert.Contains(session.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(User));
        Assert.Contains(exercise.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ExerciseDefinition));
        Assert.Contains(exercise.GetIndexes(), index => index.IsUnique
            && index.GetFilter() == "\"DeletedAt\" IS NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(WorkoutExercise.WorkoutSessionId), nameof(WorkoutExercise.Order)]));
        Assert.Contains(set.GetIndexes(), index => index.IsUnique
            && index.GetFilter() == "\"DeletedAt\" IS NULL"
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(SetEntry.WorkoutExerciseId), nameof(SetEntry.Order)]));
    }

    [Fact]
    public async Task Round_trip_persists_tombstones_and_read_store_hides_them_in_exact_order()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var owner = User.Create($"workout-{Guid.NewGuid():N}@example.com", "hash");
        var definition = ExerciseDefinition.CreateSystem("Persistence Press", BodyPart.Chest, TrackingMode.Weighted);
        var deletedDefinition = ExerciseDefinition.CreateSystem("Deleted Persistence Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
        var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now);
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
        var deletedSetId = Guid.NewGuid();
        workout.CompleteSet(itemId, deletedSetId, new SetMeasurement(50m, null, 10), now.AddMinutes(1));
        workout.CompleteSet(itemId, Guid.NewGuid(), new SetMeasurement(55m, null, 8), now.AddMinutes(2));
        workout.DeleteSet(itemId, deletedSetId, now.AddMinutes(3));
        var deletedItemId = Guid.NewGuid();
        workout.AddExercise(deletedItemId, deletedDefinition.Id, TrackingMode.Weighted, 1);
        workout.CompleteSet(deletedItemId, Guid.NewGuid(), new SetMeasurement(40m, null, 12), now.AddMinutes(4));
        workout.DeleteExercise(deletedItemId, now.AddMinutes(5));
        workout.Complete(now.AddMinutes(6));

        var deletedWorkout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now.AddHours(1));
        var deletedWorkoutItemId = Guid.NewGuid();
        deletedWorkout.AddExercise(deletedWorkoutItemId, definition.Id, TrackingMode.Weighted, 0);
        deletedWorkout.CompleteSet(deletedWorkoutItemId, Guid.NewGuid(), new SetMeasurement(30m, null, 10), now.AddHours(1).AddMinutes(1));
        deletedWorkout.Complete(now.AddHours(1).AddMinutes(2));
        deletedWorkout.Delete(now.AddHours(1).AddMinutes(3));

        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddRangeAsync(definition, deletedDefinition);
        await database.Db.WorkoutSessions.AddRangeAsync(workout, deletedWorkout);
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var read = await database.Db.GetOwnedWorkoutAsync(owner.Id, workout.Id, default);
        var persistedSetEntries = await database.Db.SetEntries.AsNoTracking().ToListAsync();
        var persistedExerciseEntries = await database.Db.WorkoutExercises.AsNoTracking()
            .Where(exercise => exercise.WorkoutSessionId == workout.Id)
            .ToListAsync();

        Assert.NotNull(read);
        Assert.Single(read!.Exercises);
        Assert.Single(read.Exercises[0].Sets);
        Assert.Equal(55m, read.Exercises[0].Sets[0].WeightKg);
        Assert.Null(await database.Db.GetOwnedWorkoutAsync(owner.Id, deletedWorkout.Id, default));
        Assert.Equal(4, persistedSetEntries.Count);
        Assert.Contains(persistedSetEntries, set => set.Id == deletedSetId && set.DeletedAt != null);
        Assert.Contains(persistedExerciseEntries, exercise => exercise.Id == deletedItemId && exercise.DeletedAt != null);
        Assert.Equal(2, persistedExerciseEntries.Count);
    }

    [Fact]
    public async Task PostgreSql_enforces_foreign_keys_active_order_set_values_and_optimistic_concurrency()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var owner = User.Create($"integrity-{Guid.NewGuid():N}@example.com", "hash");
        var definition = ExerciseDefinition.CreateSystem("Integrity Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = DateTimeOffset.UtcNow;
        var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now);
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
        var setId = Guid.NewGuid();
        workout.CompleteSet(itemId, setId, new SetMeasurement(50m, null, 10), now.AddMinutes(1));
        workout.Complete(now.AddMinutes(2));
        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddAsync(definition);
        await database.Db.WorkoutSessions.AddAsync(workout);
        await database.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_sessions (\"Id\", \"OwnerId\", \"Status\", \"StartedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {2}, {now}, {0L})"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_exercises (\"Id\", \"WorkoutSessionId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"Order\", \"Version\") VALUES ({Guid.NewGuid()}, {workout.Id}, {definition.Id}, {1}, {0}, {1L})"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO set_entries (\"Id\", \"WorkoutExerciseId\", \"Order\", \"AssistedKg\", \"Reps\", \"CompletedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {itemId}, {1}, {-1m}, {10}, {now}, {1L})"));

        await using var first = database.CreateDbContext();
        await using var second = database.CreateDbContext();
        var firstWorkout = await first.WorkoutSessions.Include("_exercises._sets").SingleAsync(item => item.Id == workout.Id);
        var secondWorkout = await second.WorkoutSessions.Include("_exercises._sets").SingleAsync(item => item.Id == workout.Id);
        firstWorkout.EditSet(itemId, setId, new SetMeasurement(52.5m, null, 10), now.AddMinutes(3));
        secondWorkout.EditSet(itemId, setId, new SetMeasurement(55m, null, 10), now.AddMinutes(3));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Add_workouts_migration_is_latest_reversible_and_matches_snapshot()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var ids = migrations.Migrations.Keys.ToArray();
        var previous = Array.IndexOf(ids, "20260815143000_AddCustomExerciseSyncIdentityAndLibraryImage");
        var current = Array.FindIndex(ids, id => id.EndsWith("_AddWorkouts", StringComparison.Ordinal));

        Assert.True(current > previous);
        var migration = migrations.CreateMigration(migrations.Migrations[ids[current]], database.Db.Database.ProviderName!)!;
        Assert.Equal(3, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Count());
        Assert.Equal(3, migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>().Count());
        Assert.Equal(
            DescribeRelationalModel(migrations.ModelSnapshot!.Model),
            DescribeRelationalModel(migration.TargetModel));
        Assert.Empty(await database.Db.Database.GetPendingMigrationsAsync());
        Assert.False(database.Db.Database.HasPendingModelChanges());
    }

    private static IReadOnlyList<string> DescribeRelationalModel(IReadOnlyModel model)
    {
        var modelPrefix = $"model|default-schema={model.GetDefaultSchema()}|{DescribeAnnotations(model)}";
        return new[] { modelPrefix }
            .Concat(model.GetEntityTypes()
                .OrderBy(entity => entity.Name, StringComparer.Ordinal)
                .SelectMany(entity =>
                {
                    var prefix = $"entity|{entity.Name}|{entity.GetSchema()}|{entity.GetTableName()}|{DescribeAnnotations(entity)}";
                    var properties = entity.GetProperties().OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(property => $"property|{entity.Name}|{property.Name}|{property.ClrType.AssemblyQualifiedName}|{property.IsNullable}|{property.ValueGenerated}|{property.GetColumnType()}|{property.GetMaxLength()}|{property.GetPrecision()}|{property.GetScale()}|{DescribeAnnotations(property)}");
                    var keys = entity.GetKeys().OrderBy(key => string.Join(',', key.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                        .Select(key => $"key|{entity.Name}|{key.IsPrimaryKey()}|{string.Join(',', key.Properties.Select(property => property.Name))}|{key.GetName()}|{DescribeAnnotations(key)}");
                    var foreignKeys = entity.GetForeignKeys().OrderBy(foreignKey => string.Join(',', foreignKey.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                        .Select(foreignKey => $"foreign-key|{entity.Name}|{string.Join(',', foreignKey.Properties.Select(property => property.Name))}|{foreignKey.PrincipalEntityType.Name}|{string.Join(',', foreignKey.PrincipalKey.Properties.Select(property => property.Name))}|{foreignKey.DeleteBehavior}|{foreignKey.IsRequired}|{foreignKey.GetConstraintName()}|{DescribeAnnotations(foreignKey)}");
                    var indexes = entity.GetIndexes().OrderBy(index => string.Join(',', index.Properties.Select(property => property.Name)), StringComparer.Ordinal)
                        .Select(index => $"index|{entity.Name}|{string.Join(',', index.Properties.Select(property => property.Name))}|{index.IsUnique}|{index.GetDatabaseName()}|{index.GetFilter()}|{DescribeAnnotations(index)}");
                    var checks = entity.GetCheckConstraints().OrderBy(check => check.Name, StringComparer.Ordinal)
                        .Select(check => $"check|{entity.Name}|{check.Name}|{check.Sql}|{DescribeAnnotations(check)}");
                    return new[] { prefix }.Concat(properties).Concat(keys).Concat(foreignKeys).Concat(indexes).Concat(checks);
                }))
            .ToArray();
    }

    private static string DescribeAnnotations(IReadOnlyAnnotatable annotatable) => string.Join(
        ',',
        annotatable.GetAnnotations()
            .OrderBy(annotation => annotation.Name, StringComparer.Ordinal)
            .Select(annotation => $"{annotation.Name}={annotation.Value}"));
}
