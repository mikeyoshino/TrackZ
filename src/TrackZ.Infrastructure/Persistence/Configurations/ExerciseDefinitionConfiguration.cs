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
        builder.HasIndex(exercise => new { exercise.OwnerId, exercise.NormalizedName })
            .IsUnique()
            .HasFilter("\"OwnerId\" IS NOT NULL AND NOT \"IsArchived\"");
        builder.HasAlternateKey(exercise => new { exercise.Id, exercise.TrackingMode });
        builder.ToTable(table => table.HasCheckConstraint("CK_exercise_definitions_tracking_mode", "\"TrackingMode\" IN (1, 2, 3)"));
        builder.HasMany<ExerciseImage>().WithOne().HasForeignKey(image => image.ExerciseDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}
