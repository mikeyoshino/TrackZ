using TrackZ.Domain.Exercises;

namespace TrackZ.Domain.Tests.Exercises;

public sealed class ImageUploadTicketTests
{
    [Fact]
    public void Expired_processing_lease_can_be_reclaimed_but_active_lease_cannot()
    {
        var now = DateTimeOffset.Parse("2026-08-15T00:00:00Z");
        var ticket = ImageUploadTicket.Create(Guid.NewGuid(), Guid.NewGuid(), "staging/a/b", "image/jpeg", 100, now.AddMinutes(5));

        Assert.True(ticket.TryClaim(now, TimeSpan.FromMinutes(1)));
        Assert.False(ticket.TryClaim(now.AddSeconds(30), TimeSpan.FromMinutes(1)));
        Assert.True(ticket.TryClaim(now.AddMinutes(2), TimeSpan.FromMinutes(1)));
    }
}
