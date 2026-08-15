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

    [Fact]
    public void Completed_ticket_cleanup_claim_allows_only_its_staging_key()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(5));
        ticket.TryMarkUploaded(now);
        Assert.True(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
        ticket.Complete(Guid.NewGuid(), ticket.ProcessingLeaseId!.Value, now);

        Assert.False(ticket.TryClaimCleanup(now, "staging/a/b", Guid.NewGuid(), out _));
        Assert.True(ticket.TryClaimCleanup(now, "staging/a/b", null, out var claim));
        Assert.True(ticket.CompleteCleanupClaim(claim));
        Assert.Equal(ImageUploadState.Completed, ticket.State);
    }

    [Fact]
    public void Active_cleanup_claim_is_not_stolen_but_stale_claim_is_reclaimed()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(1));
        Assert.True(ticket.TryClaimCleanup(now.AddMinutes(2), "staging/a/b", null, out var first));

        Assert.False(ticket.TryClaimCleanup(now.AddMinutes(6), "staging/a/b", null, out _));
        Assert.True(ticket.TryClaimCleanup(now.AddMinutes(8), "staging/a/b", null, out var replacement));
        Assert.NotEqual(first, replacement);
    }

    [Fact]
    public void Three_expired_upload_claims_each_require_cleanup_before_the_next_reclaim()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(
            Guid.NewGuid(), Guid.NewGuid(), "staging/initial", "image/jpeg", 100, now.AddMinutes(30));
        var cleanedKeys = new List<string>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var claimTime = now.AddMinutes(attempt * 3);
            Assert.True(ticket.TryClaimUpload(claimTime, TimeSpan.FromMinutes(1), out _, out var stagingKey));

            Assert.False(ticket.TryClaimUpload(
                claimTime.AddMinutes(2), TimeSpan.FromMinutes(1), out _, out _));
            Assert.Equal(stagingKey, ticket.CleanupStagingObjectKey);
            Assert.True(ticket.TryClaimCleanup(
                claimTime.AddMinutes(2), stagingKey, null, out var cleanupClaim));
            Assert.True(ticket.CompleteCleanupClaim(cleanupClaim));
            cleanedKeys.Add(stagingKey);
        }

        Assert.Equal(3, cleanedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.Null(ticket.CleanupStagingObjectKey);
    }
}
