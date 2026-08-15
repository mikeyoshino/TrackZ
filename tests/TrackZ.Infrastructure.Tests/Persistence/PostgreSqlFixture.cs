using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Infrastructure.Tests.Persistence;

internal sealed class PostgreSqlFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    private PostgreSqlFixture(PostgreSqlContainer container, AppDbContext db, string connectionString)
    {
        _container = container;
        Db = db;
        ConnectionString = connectionString;
    }

    public AppDbContext Db { get; }

    public string ConnectionString { get; }

    public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString)
        .Options);

    public static async Task<PostgreSqlFixture> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("trackz_tests")
            .WithUsername("trackz")
            .WithPassword("trackz_tests_only")
            .Build();

        try
        {
            await container.StartAsync();

            var connectionString = container.GetConnectionString();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            var db = new AppDbContext(options);

            try
            {
                await db.Database.MigrateAsync();
                return new PostgreSqlFixture(container, db, connectionString);
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
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
