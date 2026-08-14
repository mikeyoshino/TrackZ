using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Identity.Register;
using TrackZ.Infrastructure.Identity;
using TrackZ.Infrastructure.Persistence;
using Xunit.Sdk;

namespace TrackZ.Application.Tests.Identity;

public sealed class RegisterHandlerTests
{
    [Fact]
    public async Task Register_creates_a_trimmed_user_with_a_non_plaintext_password_hash()
    {
        await using var database = await RegistrationDatabase.StartAsync();
        var handler = new RegisterHandler(database.Db, new PasswordHasher());

        var result = await handler.Handle(
            new RegisterCommand("  Athlete@Example.Com  ", "ValidPassword!42"),
            CancellationToken.None);

        var user = await database.Db.Users.SingleAsync();
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("Athlete@Example.Com", result.Email);
        Assert.Equal("ATHLETE@EXAMPLE.COM", user.NormalizedEmail);
        Assert.NotEqual("ValidPassword!42", user.PasswordHash);
        Assert.DoesNotContain("ValidPassword!42", user.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_rejects_case_insensitive_normalized_duplicates()
    {
        await using var database = await RegistrationDatabase.StartAsync();
        var handler = new RegisterHandler(database.Db, new PasswordHasher());

        _ = await handler.Handle(new RegisterCommand("athlete@example.com", "ValidPassword!42"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new RegisterCommand(" ATHLETE@EXAMPLE.COM ", "AnotherPassword!42"),
            CancellationToken.None));

        Assert.Equal(10002, (int)exception.Code);
        Assert.Single(await database.Db.Users.ToListAsync());
    }

    [Theory]
    [InlineData("short!A1")]
    [InlineData("alllowercasepassword!1")]
    [InlineData("ALLUPPERCASEPASSWORD!1")]
    [InlineData("NoDigitsOrSymbolsHere")]
    public async Task Register_enforces_the_explicit_password_policy(string password)
    {
        await using var database = await RegistrationDatabase.StartAsync();
        var handler = new RegisterHandler(database.Db, new PasswordHasher());

        var exception = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new RegisterCommand("athlete@example.com", password),
            CancellationToken.None));

        Assert.Equal(10006, (int)exception.Code);
        Assert.Empty(await database.Db.Users.ToListAsync());
    }

    private sealed class RegistrationDatabase : IAsyncDisposable
    {
        private readonly PostgreSqlContainer _container;

        private RegistrationDatabase(PostgreSqlContainer container, AppDbContext db)
        {
            _container = container;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<RegistrationDatabase> StartAsync()
        {
            var container = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("trackz_application_tests")
                .WithUsername("trackz")
                .WithPassword("trackz_application_tests_only")
                .Build();

            try
            {
                await container.StartAsync();
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(container.GetConnectionString())
                    .Options;
                var db = new AppDbContext(options);
                await db.Database.MigrateAsync();
                return new RegistrationDatabase(container, db);
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
}
