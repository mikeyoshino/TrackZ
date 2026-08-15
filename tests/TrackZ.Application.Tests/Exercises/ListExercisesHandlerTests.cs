using DotNet.Testcontainers.Builders;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
using TrackZ.Application.Exercises.ListExercises;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Application.Tests.Exercises;

public sealed class ListExercisesHandlerTests
{
    [Fact]
    public async Task List_returns_visible_catalog_with_current_users_last_and_personal_record()
    {
        await using var database = await CatalogDatabase.StartAsync();
        var currentUserId = Guid.NewGuid();
        var press = ExerciseDefinition.CreateSystem("Incline Barbell Bench Press", BodyPart.Chest, TrackingMode.Weighted);
        var hidden = ExerciseDefinition.CreateCustom(Guid.NewGuid(), "Someone Else's Press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddRangeAsync(press, hidden);
        await database.Db.ExercisePerformances.AddAsync(ExercisePerformance.Create(
            currentUserId,
            press.Id, TrackingMode.Weighted,
            new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
            new ExercisePerformanceSet(70m, null, 8),
            new ExercisePerformanceSet(75m, null, 5)));
        await database.Db.SaveChangesAsync();

        var handler = new ListExercisesHandler(database.Db, new TestCurrentUser(currentUserId), new TestCursorCodec());

        var page = await handler.Handle(new ListExercisesQuery(BodyPart.Chest, null, null, 20), CancellationToken.None);

        var result = Assert.Single(page.Items);
        Assert.Equal(press.Id, result.Id);
        Assert.Equal(70m, result.LastBestSet!.WeightKg);
        Assert.Equal(75m, result.AllTimeBest!.WeightKg);
        Assert.Null(result.LastBestSet.AssistedKg);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero), result.LastPerformedAt);
    }

    [Fact]
    public async Task List_projects_images_and_performance_in_one_database_roundtrip()
    {
        var counter = new QueryCounter();
        await using var database = await CatalogDatabase.StartAsync(counter);
        var userId = Guid.NewGuid();
        var exercise = ExerciseDefinition.CreateSystem("Bench Press", BodyPart.Chest, TrackingMode.Weighted);
        await database.Db.Exercises.AddAsync(exercise);
        await database.Db.ExercisePerformances.AddAsync(ExercisePerformance.Create(
            userId, exercise.Id, TrackingMode.Weighted, DateTimeOffset.UtcNow,
            new ExercisePerformanceSet(60m, null, 8),
            new ExercisePerformanceSet(70m, null, 5)));
        await database.Db.SaveChangesAsync();
        counter.Reset();

        var handler = new ListExercisesHandler(database.Db, new TestCurrentUser(userId), new TestCursorCodec());
        _ = await handler.Handle(new ListExercisesQuery(null, null, null, 30), CancellationToken.None);

        Assert.Equal(1, counter.ReaderCommandCount);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class TestCursorCodec : IExerciseCursorCodec
    {
        public CatalogCursor Decode(string cursor) => throw new NotImplementedException();

        public string Encode(CatalogCursor cursor) => throw new NotImplementedException();
    }

    private sealed class CatalogDatabase : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _container;

        private CatalogDatabase(PostgreSqlContainer container, AppDbContext db)
        {
            _container = container;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<CatalogDatabase> StartAsync(DbCommandInterceptor? interceptor = null)
        {
            var container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("trackz_catalog_tests")
                .WithUsername("trackz")
                .WithPassword("trackz_catalog_tests_only")
                .Build();

            try
            {
                await container.StartAsync();
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(container.GetConnectionString());
                if (interceptor is not null) optionsBuilder.AddInterceptors(interceptor);
                var options = optionsBuilder.Options;
                var db = new AppDbContext(options);
                await db.Database.MigrateAsync();
                return new CatalogDatabase(container, db);
            }
            catch (DockerUnavailableException exception)
            {
                await container.DisposeAsync();
                throw SkipException.ForSkip($"Docker is unavailable; PostgreSQL integration tests require Docker. {exception.Message}");
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _container.DisposeAsync();
        }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }

        public void Reset() => ReaderCommandCount = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
