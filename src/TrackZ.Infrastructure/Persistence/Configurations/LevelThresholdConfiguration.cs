using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Gamification;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class LevelThresholdConfiguration : IEntityTypeConfiguration<LevelThreshold>
{
    public void Configure(EntityTypeBuilder<LevelThreshold> builder)
    {
        builder.ToTable("level_thresholds", table =>
        {
            table.HasCheckConstraint("CK_level_thresholds_level", "\"Level\" >= 1");
            table.HasCheckConstraint("CK_level_thresholds_required_xp", "\"RequiredXp\" >= 0");
            table.HasCheckConstraint("CK_level_thresholds_rules_version", "\"RulesVersion\" >= 1");
        });
        builder.HasKey(threshold => threshold.Id);
        builder.HasIndex(threshold => new { threshold.RulesVersion, threshold.Level }).IsUnique();
        builder.HasIndex(threshold => new { threshold.RulesVersion, threshold.RequiredXp }).IsUnique();
        builder.HasData(LevelThresholdSeeder.Version1);
    }
}
