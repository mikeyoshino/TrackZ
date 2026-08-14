using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Domain.Identity;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class IdentityPersistenceTests
{
    [Fact]
    public async Task Persistence_port_translates_normalized_email_unique_violations_to_false()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        IAppDbContext port = database.Db;

        Assert.True(await port.TryAddUserAsync(User.Create("athlete@example.com", "hash-one")));

        var inserted = await port.TryAddUserAsync(User.Create("ATHLETE@example.com", "hash-two"));

        Assert.False(inserted);
        Assert.Single(await database.Db.Users.ToListAsync());
    }

    [Fact]
    public async Task Persistence_port_rethrows_database_failures_that_are_not_normalized_email_duplicates()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        IAppDbContext port = database.Db;
        var oversizedEmail = $"{new string('a', 310)}@example.com";

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            port.TryAddUserAsync(User.Create(oversizedEmail, "hash")));
    }

    [Fact]
    public async Task Device_migration_revokes_active_legacy_refresh_tokens_and_assigns_a_valid_legacy_device()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        await database.Db.Database.MigrateAsync("20260814175047_InitialIdentity");
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string email = "legacy@example.com";
        const string normalizedEmail = "LEGACY@EXAMPLE.COM";
        const string passwordHash = "hash";
        const string tokenHash = "legacy-hash";
        DateTimeOffset? revokedAt = null;
        await database.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO users (\"Id\", \"Email\", \"NormalizedEmail\", \"PasswordHash\", \"CreatedAt\") VALUES ({userId}, {email}, {normalizedEmail}, {passwordHash}, {now})");
        await database.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO refresh_tokens (\"Id\", \"UserId\", \"TokenHash\", \"SessionId\", \"ExpiresAt\", \"CreatedAt\", \"RevokedAt\") VALUES ({tokenId}, {userId}, {tokenHash}, {sessionId}, {now.AddDays(1)}, {now}, {revokedAt})");

        await database.Db.Database.MigrateAsync();
        database.Db.ChangeTracker.Clear();
        var token = await database.Db.RefreshTokens.SingleAsync();
        Assert.Equal("LEGACY", token.DeviceName);
        Assert.NotNull(token.RevokedAt);
        Assert.InRange(token.DeviceName.Length, 1, 128);
    }

    [Fact]
    public async Task Identity_schema_is_created_from_the_initial_migration()
    {
        await using var database = await PostgreSqlFixture.StartAsync();

        var appliedMigrations = await database.Db.Database.GetAppliedMigrationsAsync();

        Assert.Contains(appliedMigrations, migration => migration.EndsWith("InitialIdentity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task User_email_is_trimmed_normalized_and_created_at_is_utc()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var createdAt = new DateTimeOffset(2026, 8, 14, 20, 15, 0, TimeSpan.FromHours(7));

        await database.Db.Users.AddAsync(User.Create("  Athlete@Example.Com  ", "password-hash", createdAt));
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var user = await database.Db.Users.SingleAsync();

        Assert.Equal("Athlete@Example.Com", user.Email);
        Assert.Equal("ATHLETE@EXAMPLE.COM", user.NormalizedEmail);
        Assert.Equal(TimeSpan.Zero, user.CreatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 13, 15, 0, TimeSpan.Zero), user.CreatedAt);
    }

    [Fact]
    public async Task Refresh_token_persists_its_hash_and_owner_without_a_raw_token_property()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var user = User.Create("athlete@example.com", "password-hash");
        var sessionId = Guid.NewGuid();
        var token = RefreshToken.Create(
            user.Id,
            "a4d2c3b1d4e5f60789a0b1c2d3e4f506a7b8c9d0e1f2030405060708090a0b1c",
            sessionId,
            DateTimeOffset.UtcNow.AddDays(7));

        await database.Db.Users.AddAsync(user);
        await database.Db.RefreshTokens.AddAsync(token);
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        var persistedToken = await database.Db.RefreshTokens
            .Include(refreshToken => refreshToken.User)
            .SingleAsync();

        Assert.Equal(user.Id, persistedToken.UserId);
        Assert.Equal(user.Id, persistedToken.User.Id);
        Assert.Equal(sessionId, persistedToken.SessionId);
        Assert.Equal(token.TokenHash, persistedToken.TokenHash);
        Assert.Null(typeof(RefreshToken).GetProperty("Token"));
    }

    [Fact]
    public async Task Deleting_a_user_cascades_to_its_refresh_tokens()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        var user = User.Create("athlete@example.com", "password-hash");
        var token = RefreshToken.Create(
            user.Id,
            "b4d2c3b1d4e5f60789a0b1c2d3e4f506a7b8c9d0e1f2030405060708090a0b1c",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddDays(7));

        await database.Db.Users.AddAsync(user);
        await database.Db.RefreshTokens.AddAsync(token);
        await database.Db.SaveChangesAsync();

        database.Db.Users.Remove(user);
        await database.Db.SaveChangesAsync();
        database.Db.ChangeTracker.Clear();

        Assert.Empty(await database.Db.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task User_email_is_unique_case_insensitively()
    {
        await using var database = await PostgreSqlFixture.StartAsync();
        await database.Db.Users.AddAsync(User.Create("athlete@example.com", "hash-one"));
        await database.Db.SaveChangesAsync();

        await database.Db.Users.AddAsync(User.Create("ATHLETE@example.com", "hash-two"));

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Db.SaveChangesAsync());
    }
}
