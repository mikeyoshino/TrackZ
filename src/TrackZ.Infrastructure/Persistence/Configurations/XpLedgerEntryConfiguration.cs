using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Gamification;
using TrackZ.Domain.Identity;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class XpLedgerEntryConfiguration : IEntityTypeConfiguration<XpLedgerEntry>
{
    public void Configure(EntityTypeBuilder<XpLedgerEntry> builder)
    {
        builder.ToTable("xp_ledger_entries", table =>
        {
            table.HasCheckConstraint("CK_xp_ledger_entries_reason", "\"Reason\" IN (1, 2, 3, 4)");
            table.HasCheckConstraint(
                "CK_xp_ledger_entries_amount",
                "\"Amount\" <> 0 AND (\"Reason\" = 4 OR \"Amount\" > 0)");
        });
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Reason).IsRequired();
        builder.Property(entry => entry.Amount).IsRequired();
        builder.Property(entry => entry.CreatedAt).IsRequired();
        builder.HasIndex(entry => new { entry.UserId, entry.Reason, entry.SourceId }).IsUnique();
        builder.HasIndex(entry => new { entry.UserId, entry.OriginId, entry.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(entry => entry.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
