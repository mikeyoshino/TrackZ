using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Gamification;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class BadgeDefinitionConfiguration : IEntityTypeConfiguration<BadgeDefinition>
{
    public void Configure(EntityTypeBuilder<BadgeDefinition> builder)
    {
        builder.ToTable("badge_definitions", table =>
        {
            table.HasCheckConstraint("CK_badge_definitions_criteria", "\"Criteria\" IN (1, 2, 3, 4)");
            table.HasCheckConstraint("CK_badge_definitions_threshold", "\"Threshold\" >= 1");
            table.HasCheckConstraint("CK_badge_definitions_version", "\"CriteriaVersion\" >= 1");
        });
        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Key).HasMaxLength(50).IsRequired();
        builder.Property(definition => definition.NameResourceKey).HasMaxLength(100).IsRequired();
        builder.Property(definition => definition.DescriptionResourceKey).HasMaxLength(100).IsRequired();
        builder.Property(definition => definition.IconKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(definition => definition.Key).IsUnique();
        builder.HasData(BadgeDefinitionSeeder.Version1);
    }
}
