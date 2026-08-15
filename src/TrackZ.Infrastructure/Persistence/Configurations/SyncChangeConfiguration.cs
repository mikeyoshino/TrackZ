using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Sync;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class SyncChangeConfiguration : IEntityTypeConfiguration<SyncChange>
{
    public void Configure(EntityTypeBuilder<SyncChange> builder)
    {
        builder.ToTable("sync_changes", table =>
        {
            table.HasCheckConstraint("CK_sync_changes_sequence", "\"Sequence\" > 0");
            table.HasCheckConstraint("CK_sync_changes_server_version", "\"ServerVersion\" >= 0");
            table.HasCheckConstraint("CK_sync_changes_payload", "jsonb_typeof(\"PayloadJson\") = 'object'");
        });
        builder.HasKey(change => change.Sequence);
        builder.Property(change => change.Sequence).UseIdentityAlwaysColumn();
        builder.Property(change => change.EntityType).HasMaxLength(32).IsRequired();
        builder.Property(change => change.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(change => change.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(change => new { change.OwnerId, change.OperationId }).IsUnique();
        builder.HasIndex(change => new { change.OwnerId, change.Sequence });
    }
}
