using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Progress;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class ExerciseDefinitionConfiguration : IEntityTypeConfiguration<ExerciseDefinition>
{
    public void Configure(EntityTypeBuilder<ExerciseDefinition> builder)
    {
        builder.ToTable("exercise_definitions");
        builder.HasKey(exercise => exercise.Id);
        builder.Property(exercise => exercise.Name).HasMaxLength(100).IsRequired();
        builder.Property(exercise => exercise.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(exercise => exercise.BodyPart).IsRequired();
        builder.Property(exercise => exercise.TrackingMode).IsRequired();
        builder.Property(exercise => exercise.IsArchived).IsRequired();
        builder.Property(exercise => exercise.HasSetHistory).IsRequired();
        builder.Property(exercise => exercise.CreatedAt).IsRequired();
        builder.HasIndex(exercise => new { exercise.Name, exercise.Id });
        builder.HasIndex(exercise => new { exercise.OwnerId, exercise.IsArchived });
        builder.HasMany<ExerciseImage>().WithOne().HasForeignKey(image => image.ExerciseDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<ExercisePerformance>().WithOne().HasForeignKey(performance => performance.ExerciseDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
