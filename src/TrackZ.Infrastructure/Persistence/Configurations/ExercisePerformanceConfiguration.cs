using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Exercises;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class ExercisePerformanceConfiguration : IEntityTypeConfiguration<ExercisePerformance>
{
    public void Configure(EntityTypeBuilder<ExercisePerformance> builder)
    {
        builder.ToTable("exercise_performances");
        builder.HasKey(performance => performance.Id);
        builder.Property(performance => performance.LastPerformedAt).IsRequired(false);
        builder.Property(performance => performance.LastBestWeightKg).HasPrecision(10, 3);
        builder.Property(performance => performance.LastBestAssistedKg).HasPrecision(10, 3);
        builder.Property(performance => performance.AllTimeBestWeightKg).HasPrecision(10, 3);
        builder.Property(performance => performance.AllTimeBestAssistedKg).HasPrecision(10, 3);
        builder.HasIndex(performance => new { performance.UserId, performance.ExerciseDefinitionId }).IsUnique();
    }
}
