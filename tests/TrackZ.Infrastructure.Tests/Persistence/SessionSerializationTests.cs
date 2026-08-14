using Microsoft.EntityFrameworkCore;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Identity.Common;
using TrackZ.Application.Identity.Logout;
using TrackZ.Application.Identity.Refresh;
using TrackZ.Domain.Identity;
using TrackZ.Infrastructure.Persistence;

namespace TrackZ.Infrastructure.Tests.Persistence;

public sealed class SessionSerializationTests
{
    [Fact]
    public async Task Refresh_waiting_behind_logout_rejects_after_logout_commits()
    {
        await using var fixture = await PostgreSqlFixture.StartAsync();
        var user = User.Create("logout-first@example.com", "password-hash");
        var sessionId = Guid.NewGuid();
        await fixture.Db.Users.AddAsync(user);
        await fixture.Db.RefreshTokens.AddAsync(RefreshToken.Create(user.Id, "hash:original", sessionId, DateTimeOffset.UtcNow.AddDays(1), deviceName: "ios"));
        await fixture.Db.SaveChangesAsync();
        var connection = fixture.Db.Database.GetConnectionString()!;
        await using var logoutContext = CreateContext(connection);
        await using var refreshContext = CreateContext(connection);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logout = new LogoutHandler(new CoordinatedDb(logoutContext, null, async () => { locked.TrySetResult(); await release.Task; }));
        var refresh = new RefreshHandler(new CoordinatedDb(refreshContext, () => { refreshAttempted.TrySetResult(); return Task.CompletedTask; }, null), new TestTokenService());
        var logoutTask = logout.Handle(new LogoutCommand(user.Id, sessionId), CancellationToken.None);
        await locked.Task;
        var refreshTask = refresh.Handle(new RefreshCommand("original", "ios"), CancellationToken.None);
        await refreshAttempted.Task;
        Assert.False(refreshTask.IsCompleted);
        release.TrySetResult();
        await logoutTask;
        await Assert.ThrowsAsync<BusinessException>(() => refreshTask);
        await using var verify = CreateContext(connection);
        Assert.Empty(await verify.RefreshTokens.Where(token => token.SessionId == sessionId && token.RevokedAt == null).ToListAsync());
    }

    [Fact]
    public async Task Second_refresh_waiting_behind_first_refresh_rejects_after_the_first_commits()
    {
        await using var fixture = await PostgreSqlFixture.StartAsync();
        var user = User.Create("refresh-race@example.com", "password-hash");
        var sessionId = Guid.NewGuid();
        await fixture.Db.Users.AddAsync(user);
        await fixture.Db.RefreshTokens.AddAsync(RefreshToken.Create(user.Id, "hash:original", sessionId, DateTimeOffset.UtcNow.AddDays(1), deviceName: "ios"));
        await fixture.Db.SaveChangesAsync();
        var connection = fixture.Db.Database.GetConnectionString()!;
        await using var firstContext = CreateContext(connection);
        await using var secondContext = CreateContext(connection);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new TestTokenService();
        var first = new RefreshHandler(new CoordinatedDb(firstContext, null, async () => { locked.TrySetResult(); await release.Task; }), tokens);
        var second = new RefreshHandler(new CoordinatedDb(secondContext, () => { secondAttempted.TrySetResult(); return Task.CompletedTask; }, null), tokens);

        var firstTask = first.Handle(new RefreshCommand("original", "ios"), CancellationToken.None);
        await locked.Task;
        var secondTask = second.Handle(new RefreshCommand("original", "ios"), CancellationToken.None);
        await secondAttempted.Task;
        Assert.False(secondTask.IsCompleted);
        release.TrySetResult();
        await firstTask;
        await Assert.ThrowsAsync<BusinessException>(() => secondTask);
        await using var verify = CreateContext(connection);
        Assert.Single(await verify.RefreshTokens.Where(token => token.SessionId == sessionId && token.RevokedAt == null).ToListAsync());
    }

    [Fact]
    public async Task Logout_waiting_behind_a_locked_refresh_revokes_the_rotated_replacement()
    {
        await using var fixture = await PostgreSqlFixture.StartAsync();
        var user = User.Create("serialized@example.com", "password-hash");
        var sessionId = Guid.NewGuid();
        await fixture.Db.Users.AddAsync(user);
        await fixture.Db.RefreshTokens.AddAsync(RefreshToken.Create(user.Id, "hash:original", sessionId, DateTimeOffset.UtcNow.AddDays(1), deviceName: "ios"));
        await fixture.Db.SaveChangesAsync();

        var connectionString = fixture.Db.Database.GetConnectionString()!;
        await using var refreshContext = CreateContext(connectionString);
        await using var logoutContext = CreateContext(connectionString);
        var refreshLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logoutAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new TestTokenService();

        var refresh = new RefreshHandler(new CoordinatedDb(refreshContext, null, async () =>
        {
            refreshLocked.TrySetResult();
            await allowRefreshCommit.Task;
        }), tokens);
        var logout = new LogoutHandler(new CoordinatedDb(logoutContext, () =>
        {
            logoutAttempted.TrySetResult();
            return Task.CompletedTask;
        }, null));

        var refreshTask = refresh.Handle(new RefreshCommand("original", "ios"), CancellationToken.None);
        await refreshLocked.Task;
        var logoutTask = logout.Handle(new LogoutCommand(user.Id, sessionId), CancellationToken.None);
        await logoutAttempted.Task;
        Assert.False(logoutTask.IsCompleted);

        allowRefreshCommit.TrySetResult();
        var replacement = await refreshTask;
        await logoutTask;

        await using var verifyContext = CreateContext(connectionString);
        Assert.Empty(await verifyContext.RefreshTokens.Where(token => token.SessionId == sessionId && token.RevokedAt == null).ToListAsync());
        var verify = new RefreshHandler(verifyContext, tokens);
        await Assert.ThrowsAsync<BusinessException>(() => verify.Handle(new RefreshCommand("original", "ios"), CancellationToken.None));
        await Assert.ThrowsAsync<BusinessException>(() => verify.Handle(new RefreshCommand(replacement.RefreshToken, "ios"), CancellationToken.None));
    }

    private static AppDbContext CreateContext(string connectionString) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private sealed class CoordinatedDb(AppDbContext inner, Func<Task>? beforeSessionLock, Func<Task>? afterSessionLock) : IAppDbContext
    {
        public Task<User?> FindUserByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) => inner.FindUserByNormalizedEmailAsync(normalizedEmail, cancellationToken);
        public Task<bool> TryAddUserAsync(User user, CancellationToken cancellationToken = default) => inner.TryAddUserAsync(user, cancellationToken);
        public Task AddRefreshTokenAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) => inner.AddRefreshTokenAsync(refreshToken, cancellationToken);
        public Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken cancellationToken = default) => inner.FindRefreshTokenByHashAsync(hash, cancellationToken);
        public Task<RefreshToken?> FindRefreshTokenForUpdateAsync(string hash, CancellationToken cancellationToken = default) => inner.FindRefreshTokenForUpdateAsync(hash, cancellationToken);
        public Task<IReadOnlyList<RefreshToken>> FindActiveSessionTokensForUpdateAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default) => inner.FindActiveSessionTokensForUpdateAsync(userId, sessionId, cancellationToken);
        public Task<IAppDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => inner.BeginTransactionAsync(cancellationToken);
        public async Task AcquireSessionLockAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            if (beforeSessionLock is not null) await beforeSessionLock();
            await inner.AcquireSessionLockAsync(sessionId, cancellationToken);
            if (afterSessionLock is not null) await afterSessionLock();
        }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class TestTokenService : ITokenService
    {
        private int _counter;
        public AuthTokenPair CreateTokenPair(Guid userId, Guid sessionId) => new("access", $"replacement-{Interlocked.Increment(ref _counter)}", DateTimeOffset.UtcNow.AddMinutes(15));
        public string HashRefreshToken(string refreshToken) => $"hash:{refreshToken}";
        public DateTimeOffset GetRefreshTokenExpiration() => DateTimeOffset.UtcNow.AddDays(1);
    }
}
