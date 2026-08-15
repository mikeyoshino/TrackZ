using Microsoft.EntityFrameworkCore;
using Npgsql;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class ExerciseTrackingModeConcurrencyTests
{
    [Fact]
    public async Task Uncommitted_performance_insert_blocks_mode_change_until_the_insert_commits_then_change_is_rejected()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Concurrent Press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.SaveChangesAsync();

        await using var performanceDb = database.CreateDbContext();
        await using var performanceTransaction = await performanceDb.Database.BeginTransactionAsync();
        await performanceDb.ExercisePerformances.AddAsync(ExercisePerformance.Create(
            Guid.NewGuid(), exercise.Id, TrackingMode.Weighted, DateTimeOffset.UtcNow,
            new ExercisePerformanceSet(60m, null, 8), new ExercisePerformanceSet(70m, null, 5)));
        await performanceDb.SaveChangesAsync();

        var changeTask = ChangeModeAsync(database.ConnectionString, exercise.Id, TrackingMode.Bodyweight);
        await WaitForBlockedStatementAsync(database.ConnectionString, "UPDATE exercise_definitions");

        await performanceTransaction.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => changeTask);

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        await using var verificationDb = database.CreateDbContext();
        var persisted = await verificationDb.Exercises.FindAsync(exercise.Id);
        Assert.Equal(TrackingMode.Weighted, persisted!.TrackingMode);
        Assert.Equal(1, await verificationDb.ExercisePerformances.CountAsync());
    }

    [Fact]
    public async Task Uncommitted_mode_change_blocks_old_mode_performance_until_change_commits_then_insert_is_rejected()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var exercise = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Concurrent Press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.SaveChangesAsync();

        await using var modeDb = database.CreateDbContext();
        await using var modeTransaction = await modeDb.Database.BeginTransactionAsync();
        await modeDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_definitions SET \"TrackingMode\" = {(int)TrackingMode.Bodyweight} WHERE \"Id\" = {exercise.Id}");

        var insertTask = InsertPerformanceAsync(database.ConnectionString, exercise.Id, TrackingMode.Weighted);
        await WaitForBlockedStatementAsync(database.ConnectionString, "INSERT INTO exercise_performances");

        await modeTransaction.CommitAsync();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => insertTask);

        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await using var verificationDb = database.CreateDbContext();
        var persisted = await verificationDb.Exercises.FindAsync(exercise.Id);
        Assert.Equal(TrackingMode.Bodyweight, persisted!.TrackingMode);
        Assert.Equal(0, await verificationDb.ExercisePerformances.CountAsync());
    }

    private static async Task ChangeModeAsync(string connectionString, Guid exerciseId, TrackingMode trackingMode)
    {
        await using var db = CreateDbContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE exercise_definitions SET \"TrackingMode\" = {(int)trackingMode} WHERE \"Id\" = {exerciseId}");
        await transaction.CommitAsync();
    }

    private static async Task InsertPerformanceAsync(string connectionString, Guid exerciseId, TrackingMode trackingMode)
    {
        await using var db = CreateDbContext(connectionString);
        await db.ExercisePerformances.AddAsync(ExercisePerformance.Create(
            Guid.NewGuid(), exerciseId, trackingMode, DateTimeOffset.UtcNow,
            new ExercisePerformanceSet(60m, null, 8), new ExercisePerformanceSet(70m, null, 5)));
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateDbContext(string connectionString) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString)
        .Options);

    private static async Task WaitForBlockedStatementAsync(string connectionString, string statementFragment)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_stat_activity
                    WHERE datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND query ILIKE @statementPattern)
                """, connection);
            command.Parameters.AddWithValue("statementPattern", $"%{statementFragment}%");
            if ((bool)(await command.ExecuteScalarAsync())!) return;

            await Task.Delay(20);
        }

        throw new TimeoutException($"The statement containing '{statementFragment}' did not block within five seconds.");
    }
}
