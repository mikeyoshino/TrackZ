using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Exercises;
namespace TrackZ.Infrastructure.Persistence.Configurations;
public sealed class ImageUploadTicketConfiguration : IEntityTypeConfiguration<ImageUploadTicket>
{
 public void Configure(EntityTypeBuilder<ImageUploadTicket> builder)
 {
  builder.ToTable("image_upload_tickets", table =>
  {
   table.HasCheckConstraint("CK_image_upload_tickets_state", "\"State\" IN (1, 2, 3, 4, 5, 6)");
   table.HasCheckConstraint("CK_image_upload_tickets_processing_lease", "(\"State\" = 4 AND \"ProcessingStartedAt\" IS NOT NULL AND \"LeaseExpiresAt\" IS NOT NULL AND \"ProcessingLeaseId\" IS NOT NULL) OR (\"State\" <> 4 AND \"ProcessingStartedAt\" IS NULL AND \"LeaseExpiresAt\" IS NULL AND \"ProcessingLeaseId\" IS NULL)");
  });
  builder.HasKey(x => x.Id);
  builder.Property(x => x.StagingObjectKey).HasMaxLength(512).IsRequired();
  builder.Property(x => x.DeclaredContentType).HasMaxLength(32).IsRequired();
  builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken().IsRequired();
  builder.Property(x => x.CleanupStagingObjectKey).HasMaxLength(512);
  builder.HasIndex(x => new { x.OwnerId, x.Id });
  builder.HasOne<ExerciseDefinition>().WithMany().HasForeignKey(x => x.ExerciseDefinitionId).OnDelete(DeleteBehavior.Cascade);
  builder.HasOne<ExerciseImage>().WithMany().HasForeignKey(x => x.ExerciseImageId).OnDelete(DeleteBehavior.Restrict);
 }
}
