using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Exercises;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class ExerciseImageConfiguration : IEntityTypeConfiguration<ExerciseImage>
{
    public void Configure(EntityTypeBuilder<ExerciseImage> builder)
    {
        builder.ToTable("exercise_images");
        builder.HasKey(image => image.Id);
        builder.Property(image => image.MasterObjectKey).HasMaxLength(512).IsRequired();
        builder.Property(image => image.ThumbnailObjectKey).HasMaxLength(512).IsRequired();
        builder.Property(image => image.SourceReference).HasMaxLength(512).IsRequired();
        builder.Property(image => image.RightsReference).HasMaxLength(512);
        builder.Property(image => image.Version).IsRequired();
        builder.Property(image => image.Source).IsRequired();
        builder.Property(image => image.IsPrivate).IsRequired();
        builder.Property(image => image.ReviewState).IsRequired(false);
        builder.Property(image => image.CreatedAt).IsRequired();
        builder.Ignore(image => image.IsReadyForUse);
        builder.HasIndex(image => new { image.ExerciseDefinitionId, image.Version });
    }
}
