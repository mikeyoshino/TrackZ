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
    public async Task Deferred_active_order_constraints_allow_swap_middle_insert_and_delete_reindex_in_one_save()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var owner = User.Create($"reorder-{Guid.NewGuid():N}@example.com", "hash");
        var definitions = Enumerable.Range(0, 4)
            .Select(index => ExerciseDefinition.CreateSystem($"Deferred Press {index}", BodyPart.Chest, TrackingMode.Weighted))
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now);
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < ids.Length; index++)
            workout.AddExercise(ids[index], definitions[index].Id, TrackingMode.Weighted, index);
        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddRangeAsync(definitions);
        await database.Db.WorkoutSessions.AddAsync(workout);
        await database.Db.SaveChangesAsync();

        workout.ReorderExercises([ids[2], ids[0], ids[1]]);
        await database.Db.SaveChangesAsync();
        var insertedId = Guid.NewGuid();
        workout.AddExercise(insertedId, definitions[3].Id, TrackingMode.Weighted, 1);
        await database.Db.SaveChangesAsync();
        workout.DeleteExercise(ids[0], now.AddMinutes(1));
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var rows = await database.Db.WorkoutExercises.AsNoTracking()
            .Where(exercise => exercise.WorkoutSessionId == workout.Id && exercise.DeletedAt == null)
            .OrderBy(exercise => exercise.Order)
            .ToListAsync();
        Assert.Equal([ids[2], insertedId, ids[1]], rows.Select(exercise => exercise.Id));
        Assert.Equal([0, 1, 2], rows.Select(exercise => exercise.Order));
    }

    [Fact]
    public async Task Deferred_active_set_order_allows_delete_and_reindex_in_one_save()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var owner = User.Create($"set-reindex-{Guid.NewGuid():N}@example.com", "hash");
        var definition = ExerciseDefinition.CreateSystem("Deferred Set Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = DateTimeOffset.UtcNow;
        var workout = WorkoutSession.Start(owner.Id, Guid.NewGuid(), now);
        var itemId = Guid.NewGuid();
        workout.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
        var setIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < setIds.Length; index++)
            workout.CompleteSet(itemId, setIds[index], new SetMeasurement(50m + index, null, 10), now.AddMinutes(index + 1));
        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddAsync(definition);
        await database.Db.WorkoutSessions.AddAsync(workout);
        await database.Db.SaveChangesAsync();

        workout.DeleteSet(itemId, setIds[0], now.AddMinutes(4));
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var rows = await database.Db.SetEntries.AsNoTracking()
            .Where(set => set.WorkoutExerciseId == itemId && set.DeletedAt == null)
            .OrderBy(set => set.Order)
            .ToListAsync();
        Assert.Equal(setIds.Skip(1), rows.Select(set => set.Id));
        Assert.Equal([0, 1], rows.Select(set => set.Order));
    }

    [Fact]
    public async Task Exercise_history_skips_newest_session_when_all_its_sets_are_tombstoned()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var owner = User.Create($"history-tombstone-{Guid.NewGuid():N}@example.com", "hash");
        var definition = ExerciseDefinition.CreateSystem("History Tombstone Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = DateTimeOffset.UtcNow;
        WorkoutSession Completed(DateTimeOffset completedAt, decimal weight, out Guid itemId, out Guid setId)
        {
            var session = WorkoutSession.Start(owner.Id, Guid.NewGuid(), completedAt.AddMinutes(-5));
            itemId = Guid.NewGuid();
            setId = Guid.NewGuid();
            session.AddExercise(itemId, definition.Id, TrackingMode.Weighted, 0);
            session.CompleteSet(itemId, setId, new SetMeasurement(weight, null, 10), completedAt.AddMinutes(-1));
            session.Complete(completedAt);
            return session;
        }
        var prior = Completed(now.AddDays(-7), 60m, out _, out _);
        var newest = Completed(now, 70m, out var newestItemId, out var newestSetId);
        newest.DeleteSet(newestItemId, newestSetId, now.AddMinutes(1));
        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddAsync(definition);
        await database.Db.WorkoutSessions.AddRangeAsync(prior, newest);
        await database.Db.SaveChangesAsync();

        var rows = await database.Db.ListOwnedExerciseHistoryAsync(owner.Id, definition.Id, null, 20, default);

        var item = Assert.Single(rows);
        Assert.Equal(prior.Id, item.Id);
        Assert.Equal(60m, Assert.Single(Assert.Single(item.Exercises).Sets).WeightKg);
    }

    [Fact]
    public async Task Hardening_constraints_reject_negative_orders_invalid_lifecycle_and_mode_shape()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var owner = User.Create($"hardening-{Guid.NewGuid():N}@example.com", "hash");
        var definition = ExerciseDefinition.CreateSystem("Hardening Press", BodyPart.Chest, TrackingMode.Weighted);
        var now = DateTimeOffset.UtcNow;
        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddAsync(definition);
        await database.Db.SaveChangesAsync();

        var lifecycle = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_sessions (\"Id\", \"OwnerId\", \"Status\", \"StartedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {owner.Id}, {3}, {now}, {0L})"));
        Assert.Equal("CK_workout_sessions_lifecycle", lifecycle.ConstraintName);
        var activeWithCompletion = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_sessions (\"Id\", \"OwnerId\", \"Status\", \"StartedAt\", \"CompletedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {owner.Id}, {2}, {now}, {now}, {0L})"));
        Assert.Equal("CK_workout_sessions_lifecycle", activeWithCompletion.ConstraintName);

        var workoutId = Guid.NewGuid();
        await database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_sessions (\"Id\", \"OwnerId\", \"Status\", \"StartedAt\", \"Version\") VALUES ({workoutId}, {owner.Id}, {2}, {now}, {0L})");
        var negativeExerciseOrder = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_exercises (\"Id\", \"WorkoutSessionId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"Order\", \"Version\") VALUES ({Guid.NewGuid()}, {workoutId}, {definition.Id}, {1}, {-1}, {1L})"));
        Assert.Equal("CK_workout_exercises_order", negativeExerciseOrder.ConstraintName);

        var itemId = Guid.NewGuid();
        await database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_exercises (\"Id\", \"WorkoutSessionId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"Order\", \"Version\") VALUES ({itemId}, {workoutId}, {definition.Id}, {1}, {0}, {1L})");
        var negativeSetOrder = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO set_entries (\"Id\", \"WorkoutExerciseId\", \"TrackingMode\", \"Order\", \"WeightKg\", \"Reps\", \"CompletedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {itemId}, {1}, {-1}, {50m}, {10}, {now}, {1L})"));
        Assert.Equal("CK_set_entries_order", negativeSetOrder.ConstraintName);
        var invalidModeShape = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO set_entries (\"Id\", \"WorkoutExerciseId\", \"TrackingMode\", \"Order\", \"AssistedKg\", \"Reps\", \"CompletedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {itemId}, {1}, {0}, {20m}, {10}, {now}, {1L})"));
        Assert.Equal("CK_set_entries_mode_measurement", invalidModeShape.ConstraintName);
        var mismatchedParentMode = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO set_entries (\"Id\", \"WorkoutExerciseId\", \"TrackingMode\", \"Order\", \"Reps\", \"CompletedAt\", \"Version\") VALUES ({Guid.NewGuid()}, {itemId}, {2}, {0}, {10}, {now}, {1L})"));
        Assert.Equal("FK_set_entries_workout_exercises_WorkoutExerciseId_TrackingMode", mismatchedParentMode.ConstraintName);
    }
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
            && index.GetDatabaseName() == "UQ_workout_exercises_active_order"
            && index.GetFilter() is null
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(WorkoutExercise.WorkoutSessionId), "ActiveOrder"]));
        Assert.Contains(set.GetIndexes(), index => index.IsUnique
            && index.GetDatabaseName() == "UQ_set_entries_active_order"
            && index.GetFilter() is null
            && index.Properties.Select(property => property.Name).SequenceEqual([nameof(SetEntry.WorkoutExerciseId), "ActiveOrder"]));
        Assert.NotNull(set.FindProperty(nameof(SetEntry.TrackingMode)));
        Assert.Contains(set.GetForeignKeys(), foreignKey => foreignKey.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(SetEntry.WorkoutExerciseId), nameof(SetEntry.TrackingMode)]));
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
    public async Task Add_workouts_migration_remains_historical_and_reversible()
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
        Assert.Null(migration.TargetModel.FindEntityType(typeof(SetEntry))!.FindProperty(nameof(SetEntry.TrackingMode)));
        Assert.NotEqual(
            DescribeRelationalModel(migrations.ModelSnapshot!.Model),
            DescribeRelationalModel(migration.TargetModel));
    }

    [Fact]
    public async Task Hardening_migration_is_latest_deferred_reversible_and_matches_snapshot()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var migrations = database.Db.GetService<IMigrationsAssembly>();
        var ids = migrations.Migrations.Keys.ToArray();
        var addWorkouts = Array.FindIndex(ids, id => id.EndsWith("_AddWorkouts", StringComparison.Ordinal));
        var hardening = Array.FindIndex(ids, id => id.EndsWith("_HardenWorkoutPersistence", StringComparison.Ordinal));
        Assert.True(hardening > addWorkouts);
        var migration = migrations.CreateMigration(migrations.Migrations[ids[hardening]], database.Db.Database.ProviderName!)!;
        var upSql = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()
            .Select(operation => operation.Sql).ToArray();
        var downSql = migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()
            .Select(operation => operation.Sql).ToArray();

        Assert.Equal(2, upSql.Count(sql => sql.Contains("DEFERRABLE INITIALLY DEFERRED", StringComparison.Ordinal)));
        Assert.Contains(upSql, sql => sql.Contains("UPDATE set_entries", StringComparison.Ordinal));
        Assert.Equal(2, downSql.Count(sql => sql.Contains("DROP CONSTRAINT \"UQ_", StringComparison.Ordinal)));
        Assert.Equal(
            DescribeRelationalModel(migrations.ModelSnapshot!.Model),
            DescribeRelationalModel(migration.TargetModel));
        Assert.Empty(await database.Db.Database.GetPendingMigrationsAsync());
        Assert.False(database.Db.Database.HasPendingModelChanges());

        var constraints = await database.Db.Database.SqlQueryRaw<ConstraintState>(
            """
            SELECT conname AS "Name", condeferrable AS "Deferrable", condeferred AS "InitiallyDeferred"
            FROM pg_constraint
            WHERE conname IN ('UQ_workout_exercises_active_order', 'UQ_set_entries_active_order')
            ORDER BY conname
            """).ToListAsync();
        Assert.Equal(2, constraints.Count);
        Assert.All(constraints, constraint =>
        {
            Assert.True(constraint.Deferrable);
            Assert.True(constraint.InitiallyDeferred);
        });
    }

    [Fact]
    public async Task Hardening_migration_backfills_existing_set_mode_before_enforcing_composite_integrity()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        await database.Db.Database.MigrateAsync("20260815155510_AddWorkouts");
        var owner = User.Create($"migration-backfill-{Guid.NewGuid():N}@example.com", "hash");
        var definition = ExerciseDefinition.CreateSystem("Migration Backfill Press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Users.AddAsync(owner);
        await database.Db.Exercises.AddAsync(definition);
        await database.Db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var workoutId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var setId = Guid.NewGuid();
        await database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_sessions (\"Id\", \"OwnerId\", \"Status\", \"StartedAt\", \"CompletedAt\", \"Version\") VALUES ({workoutId}, {owner.Id}, {3}, {now.AddMinutes(-2)}, {now}, {1L})");
        await database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO workout_exercises (\"Id\", \"WorkoutSessionId\", \"ExerciseDefinitionId\", \"TrackingMode\", \"Order\", \"Version\") VALUES ({itemId}, {workoutId}, {definition.Id}, {(int)TrackingMode.Weighted}, {0}, {1L})");
        await database.Db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO set_entries (\"Id\", \"WorkoutExerciseId\", \"Order\", \"WeightKg\", \"Reps\", \"CompletedAt\", \"Version\") VALUES ({setId}, {itemId}, {0}, {70m}, {8}, {now.AddMinutes(-1)}, {1L})");

        await database.Db.Database.MigrateAsync();
        database.Db.ChangeTracker.Clear();

        var persisted = await database.Db.SetEntries.AsNoTracking().SingleAsync(set => set.Id == setId);
        Assert.Equal(TrackingMode.Weighted, persisted.TrackingMode);
        Assert.Empty(await database.Db.Database.GetPendingMigrationsAsync());
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

    private sealed record ConstraintState(string Name, bool Deferrable, bool InitiallyDeferred);
}
