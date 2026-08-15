using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Workouts;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    public void Configure(EntityTypeBuilder<WorkoutSession> builder)
    {
        builder.ToTable("workout_sessions", table =>
            table.HasCheckConstraint("CK_workout_sessions_status", "\"Status\" IN (1, 2, 3)"));
        builder.HasKey(workout => workout.Id);
        builder.Property(workout => workout.OwnerId).IsRequired();
        builder.Property(workout => workout.Status).IsRequired();
        builder.Property(workout => workout.StartedAt).IsRequired();
        builder.Property(workout => workout.CompletedAt);
        builder.Property(workout => workout.DeletedAt);
        builder.Property(workout => workout.Version).IsConcurrencyToken();
        builder.Ignore(workout => workout.Exercises);
        builder.Ignore(workout => workout.ExerciseEntries);
        builder.Ignore(workout => workout.IsDeleted);
        builder.HasOne<User>().WithMany().HasForeignKey(workout => workout.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<WorkoutExercise>("_exercises")
            .WithOne()
            .HasForeignKey(exercise => exercise.WorkoutSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation("_exercises").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(workout => new { workout.OwnerId, workout.CompletedAt, workout.Id });
        builder.HasIndex(workout => new { workout.OwnerId, workout.Status, workout.DeletedAt });
    }
}
