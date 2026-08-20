using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Gamification;
using TrackZ.Domain.Identity;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class StreakStateConfiguration : IEntityTypeConfiguration<StreakState>
{
    public void Configure(EntityTypeBuilder<StreakState> builder)
    {
        builder.ToTable("streak_states", table =>
        {
            table.HasCheckConstraint("CK_streak_states_current", "\"CurrentWeeks\" >= 0");
            table.HasCheckConstraint("CK_streak_states_best", "\"BestWeeks\" >= \"CurrentWeeks\"");
            table.HasCheckConstraint(
                "CK_streak_states_iso_week",
                "\"LastEvaluatedIsoYear\" >= 1 AND \"LastEvaluatedIsoWeek\" BETWEEN 1 AND 53");
        });
        builder.HasKey(state => state.Id);
        builder.HasIndex(state => state.UserId).IsUnique();
        builder.Ignore(state => state.LastEvaluatedWeek);
        builder.HasOne<User>().WithMany().HasForeignKey(state => state.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
