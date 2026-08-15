using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Progress;
using TrackZ.Domain.Exercises;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class ExercisePerformanceConfiguration : IEntityTypeConfiguration<ExercisePerformance>
{
    public void Configure(EntityTypeBuilder<ExercisePerformance> builder)
    {
        builder.ToTable("exercise_performances");
        builder.HasKey(performance => performance.Id);
        builder.Property(performance => performance.LastPerformedAt).IsRequired(false);
        builder.Property(performance => performance.TrackingMode).IsRequired();
        builder.Property(performance => performance.LastBestWeightKg).HasPrecision(10, 3);
        builder.Property(performance => performance.LastBestAssistedKg).HasPrecision(10, 3);
        builder.Property(performance => performance.AllTimeBestWeightKg).HasPrecision(10, 3);
        builder.Property(performance => performance.AllTimeBestAssistedKg).HasPrecision(10, 3);
        builder.HasIndex(performance => new { performance.UserId, performance.ExerciseDefinitionId }).IsUnique();
        builder.HasOne<ExerciseDefinition>().WithMany()
            .HasForeignKey(performance => performance.ExerciseDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_exercise_performances_tracking_mode", "\"TrackingMode\" IN (1, 2, 3)");
            table.HasCheckConstraint("CK_exercise_performances_last_best_shape", "\"LastPerformedAt\" IS NOT NULL AND \"LastBestReps\" IS NOT NULL AND \"LastBestReps\" > 0 AND ((\"TrackingMode\" = 1 AND \"LastBestWeightKg\" IS NOT NULL AND \"LastBestWeightKg\" > 0 AND \"LastBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 2 AND \"LastBestWeightKg\" IS NULL AND \"LastBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 3 AND \"LastBestWeightKg\" IS NULL AND \"LastBestAssistedKg\" IS NOT NULL AND \"LastBestAssistedKg\" > 0))");
            table.HasCheckConstraint("CK_exercise_performances_all_time_best_shape", "\"AllTimeBestReps\" IS NOT NULL AND \"AllTimeBestReps\" > 0 AND ((\"TrackingMode\" = 1 AND \"AllTimeBestWeightKg\" IS NOT NULL AND \"AllTimeBestWeightKg\" > 0 AND \"AllTimeBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 2 AND \"AllTimeBestWeightKg\" IS NULL AND \"AllTimeBestAssistedKg\" IS NULL) OR (\"TrackingMode\" = 3 AND \"AllTimeBestWeightKg\" IS NULL AND \"AllTimeBestAssistedKg\" IS NOT NULL AND \"AllTimeBestAssistedKg\" > 0))");
        });
    }
}
