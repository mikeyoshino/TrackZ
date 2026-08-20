using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Gamification;
using TrackZ.Domain.Identity;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class UserProgressConfiguration : IEntityTypeConfiguration<UserProgress>
{
    public void Configure(EntityTypeBuilder<UserProgress> builder)
    {
        builder.ToTable("user_progress", table =>
        {
            table.HasCheckConstraint("CK_user_progress_total_xp", "\"TotalXp\" >= 0");
            table.HasCheckConstraint("CK_user_progress_level", "\"Level\" >= 1");
            table.HasCheckConstraint("CK_user_progress_rules_version", "\"RulesVersion\" >= 1");
        });
        builder.HasKey(progress => progress.Id);
        builder.HasIndex(progress => progress.UserId).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(progress => progress.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
