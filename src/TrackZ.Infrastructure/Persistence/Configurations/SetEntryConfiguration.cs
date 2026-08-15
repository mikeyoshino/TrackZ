using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Workouts;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class SetEntryConfiguration : IEntityTypeConfiguration<SetEntry>
{
    public void Configure(EntityTypeBuilder<SetEntry> builder)
    {
        builder.ToTable("set_entries", table =>
        {
            table.HasCheckConstraint("CK_set_entries_reps", "\"Reps\" BETWEEN 1 AND 999");
            table.HasCheckConstraint("CK_set_entries_order", "\"Order\" >= 0");
            table.HasCheckConstraint(
                "CK_set_entries_mode_measurement",
                "(\"TrackingMode\" = 1 AND \"WeightKg\" > 0 AND \"AssistedKg\" IS NULL) OR " +
                "(\"TrackingMode\" = 2 AND \"WeightKg\" IS NULL AND \"AssistedKg\" IS NULL) OR " +
                "(\"TrackingMode\" = 3 AND \"WeightKg\" IS NULL AND \"AssistedKg\" > 0)");
        });
        builder.HasKey(set => set.Id);
        builder.Property(set => set.Id).ValueGeneratedNever();
        builder.Property(set => set.WorkoutExerciseId).IsRequired();
        builder.Property(set => set.TrackingMode).IsRequired();
        builder.Property(set => set.Order).IsRequired();
        builder.Property<int?>("ActiveOrder")
            .HasComputedColumnSql("CASE WHEN \"DeletedAt\" IS NULL THEN \"Order\" ELSE NULL END", stored: true);
        builder.Property(set => set.WeightKg).HasColumnType("numeric(8,3)");
        builder.Property(set => set.AssistedKg).HasColumnType("numeric(8,3)");
        builder.Property(set => set.Reps).IsRequired();
        builder.Property(set => set.CompletedAt).IsRequired();
        builder.Property(set => set.UpdatedAt);
        builder.Property(set => set.DeletedAt);
        builder.Property(set => set.Version).IsConcurrencyToken();
        builder.Ignore(set => set.Measurement);
        builder.Ignore(set => set.IsDeleted);
        builder.Ignore("LastMutationAt");
        builder.HasIndex(nameof(SetEntry.WorkoutExerciseId), "ActiveOrder")
            .IsUnique()
            .HasDatabaseName("UQ_set_entries_active_order");
    }
}
