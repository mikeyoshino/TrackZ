using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Tests.Exercises;

public sealed class ImageUploadTicketTests
{
    [Fact]
    public void Expired_processing_lease_can_be_reclaimed_but_active_lease_cannot()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(5));
        ticket.TryMarkUploaded(now);

        Assert.True(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
        Assert.False(ticket.TryClaim(now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.True(ticket.TryClaim(now.AddMinutes(2), TimeSpan.FromMinutes(1)));
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
        Assert.True(ticket.TryClaim(now.AddMinutes(2), TimeSpan.FromMinutes(1)));

        Assert.NotNull(firstLease);
        Assert.NotNull(ticket.ProcessingLeaseId);
        Assert.NotEqual(firstLease, ticket.ProcessingLeaseId);
    }
}
