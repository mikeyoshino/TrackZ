using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Tests.Exercises;

public sealed class ImageUploadTicketTests
{
    [Fact]
    public void Expired_processing_lease_is_blocked_until_its_attempt_cleanup_is_complete()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(5));
        ticket.TryMarkUploaded(now);

        Assert.True(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
        Assert.False(ticket.TryClaim(now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.False(ticket.TryClaim(now.AddMinutes(2), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Upload_must_be_recorded_before_completion_can_be_claimed()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(5));

        Assert.False(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
        Assert.True(ticket.TryMarkUploaded(now));
        Assert.Equal(ImageUploadState.Uploaded, ticket.State);
        Assert.True(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Reclaim_replaces_the_processing_lease_fencing_token()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(5));
        ticket.TryMarkUploaded(now);

        Assert.True(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
        var firstLease = ticket.ProcessingLeaseId;
        Assert.False(ticket.TryClaim(now.AddMinutes(2), TimeSpan.FromMinutes(1)));

        Assert.NotNull(firstLease);
        Assert.Null(ticket.ProcessingLeaseId);
        Assert.Equal(firstLease, ticket.CleanupProcessingLeaseId);
    }

    [Fact]
    public void Cleanup_claim_terminalizes_expired_ticket_and_prevents_repeat_claims()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(1));

        Assert.True(ticket.TryClaimCleanup(now.AddMinutes(2), "staging/a/b", null, out var cleanupClaim));
        Assert.Equal(ImageUploadState.Expired, ticket.State);
        Assert.True(ticket.CompleteCleanupClaim(cleanupClaim));
        Assert.False(ticket.TryClaimCleanup(now.AddMinutes(3), "staging/a/b", null, out _));
    }
}
