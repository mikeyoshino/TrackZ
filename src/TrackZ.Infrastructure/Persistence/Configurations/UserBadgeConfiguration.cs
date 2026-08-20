using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Gamification;
using TrackZ.Domain.Identity;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class UserBadgeConfiguration : IEntityTypeConfiguration<UserBadge>
{
    public void Configure(EntityTypeBuilder<UserBadge> builder)
    {
        builder.ToTable("user_badges");
        builder.HasKey(badge => badge.Id);
        builder.Property(badge => badge.BadgeKey).HasMaxLength(50).IsRequired();
        builder.HasIndex(badge => new { badge.UserId, badge.BadgeDefinitionId }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(badge => badge.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BadgeDefinition>().WithMany().HasForeignKey(badge => badge.BadgeDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BadgeAuditEventConfiguration : IEntityTypeConfiguration<BadgeAuditEvent>
{
    public void Configure(EntityTypeBuilder<BadgeAuditEvent> builder)
    {
        builder.ToTable("badge_audit_events", table =>
            table.HasCheckConstraint("CK_badge_audit_events_action", "\"Action\" IN (1, 2)"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.BadgeKey).HasMaxLength(50).IsRequired();
        builder.HasIndex(item => new { item.UserId, item.OccurredAt, item.Id });
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BadgeDefinition>().WithMany().HasForeignKey(item => item.BadgeDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
