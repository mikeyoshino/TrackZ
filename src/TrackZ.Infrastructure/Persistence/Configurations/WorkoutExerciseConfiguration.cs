using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Workouts;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class WorkoutExerciseConfiguration : IEntityTypeConfiguration<WorkoutExercise>
{
    public void Configure(EntityTypeBuilder<WorkoutExercise> builder)
    {
        builder.ToTable("workout_exercises", table =>
            table.HasCheckConstraint("CK_workout_exercises_tracking_mode", "\"TrackingMode\" IN (1, 2, 3)"));
        builder.HasKey(exercise => exercise.Id);
        builder.Property(exercise => exercise.WorkoutSessionId).IsRequired();
        builder.Property(exercise => exercise.ExerciseDefinitionId).IsRequired();
        builder.Property(exercise => exercise.TrackingMode).IsRequired();
        builder.Property(exercise => exercise.Order).IsRequired();
        builder.Property(exercise => exercise.DeletedAt);
        builder.Property(exercise => exercise.Version).IsConcurrencyToken();
        builder.Ignore(exercise => exercise.Sets);
        builder.Ignore(exercise => exercise.SetEntries);
        builder.Ignore(exercise => exercise.IsDeleted);
        builder.Ignore("LastMutationAt");
        builder.HasOne<ExerciseDefinition>().WithMany()
            .HasForeignKey(exercise => exercise.ExerciseDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<SetEntry>("_sets")
            .WithOne()
            .HasForeignKey(set => set.WorkoutExerciseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation("_sets").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(exercise => new { exercise.WorkoutSessionId, exercise.Order })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");
        builder.HasIndex(exercise => new { exercise.ExerciseDefinitionId, exercise.WorkoutSessionId });
    }
}
