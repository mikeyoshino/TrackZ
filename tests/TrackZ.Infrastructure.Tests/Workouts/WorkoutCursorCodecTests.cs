using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Exceptions;
using TrackZ.Application.Workouts;
using TrackZ.Contracts.Errors;
using TrackZ.Infrastructure.Identity;
using TrackZ.Infrastructure.Workouts;

namespace TrackZ.Infrastructure.Tests.Workouts;

public sealed class WorkoutCursorCodecTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid ExerciseId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cursor_round_trips_only_with_the_exact_owner_purpose_and_exercise_scope()
    {
        var time = new MutableTimeProvider(Now);
        var codec = CreateCodec(time);
        var scope = new WorkoutCursorScope(OwnerId, WorkoutCursorPurpose.ExerciseHistory, ExerciseId);
        var cursor = codec.Encode(scope, Now.AddDays(-1), Guid.NewGuid());

        var decoded = codec.Decode(cursor, scope);

        Assert.Equal(scope.OwnerId, decoded.OwnerId);
        Assert.Equal(scope.Purpose, decoded.Purpose);
        Assert.Equal(scope.ExerciseId, decoded.ExerciseId);
        Assert.Equal(Now, decoded.IssuedAt);
        Assert.Equal(Now.AddMinutes(60), decoded.ExpiresAt);
        Assert.Throws<BusinessException>(() => codec.Decode(cursor, scope with { OwnerId = Guid.NewGuid() }));
        Assert.Throws<BusinessException>(() => codec.Decode(cursor, new WorkoutCursorScope(OwnerId, WorkoutCursorPurpose.WorkoutHistory, null)));
        Assert.Throws<BusinessException>(() => codec.Decode(cursor, scope with { ExerciseId = Guid.NewGuid() }));
    }

    [Fact]
    public void Cursor_rejects_expired_or_future_issued_payloads_as_invalid_request()
    {
        var time = new MutableTimeProvider(Now);
        var codec = CreateCodec(time);
        var scope = new WorkoutCursorScope(OwnerId, WorkoutCursorPurpose.WorkoutHistory, null);
        var cursor = codec.Encode(scope, Now.AddDays(-1), Guid.NewGuid());

        time.UtcNow = Now.AddMinutes(61);
        var expired = Assert.Throws<BusinessException>(() => codec.Decode(cursor, scope));
        time.UtcNow = Now.AddMinutes(-1);
        var future = Assert.Throws<BusinessException>(() => codec.Decode(cursor, scope));

        Assert.Equal(BusinessErrorCode.InvalidRequest, expired.Code);
        Assert.Equal(BusinessErrorCode.InvalidRequest, future.Code);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void Lifetime_configuration_is_explicitly_bounded(int lifetimeMinutes)
    {
        Assert.False(new WorkoutCursorOptions { LifetimeMinutes = lifetimeMinutes }.IsValid());
    }

    private static HmacWorkoutCursorCodec CreateCodec(TimeProvider timeProvider) => new(
        Options.Create(new JwtOptions
        {
            Issuer = "trackz-api",
            Audience = "trackz-mobile",
            SigningKey = "test-signing-key-that-is-at-least-thirty-two-bytes-long",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14
        }),
        Options.Create(new WorkoutCursorOptions { LifetimeMinutes = 60 }),
        timeProvider);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
