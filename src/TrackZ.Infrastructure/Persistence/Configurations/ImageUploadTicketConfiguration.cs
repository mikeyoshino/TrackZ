using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Exercises;
namespace TrackZ.Infrastructure.Persistence.Configurations;
public sealed class ImageUploadTicketConfiguration : IEntityTypeConfiguration<ImageUploadTicket>
{
 public void Configure(EntityTypeBuilder<ImageUploadTicket> builder) { builder.ToTable("image_upload_tickets"); builder.HasKey(x => x.Id); builder.Property(x => x.StagingObjectKey).HasMaxLength(512).IsRequired(); builder.Property(x => x.DeclaredContentType).HasMaxLength(32).IsRequired(); builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken().IsRequired(); builder.HasIndex(x => new { x.OwnerId, x.Id }); builder.HasOne<ExerciseDefinition>().WithMany().HasForeignKey(x => x.ExerciseDefinitionId).OnDelete(DeleteBehavior.Cascade); builder.HasOne<ExerciseImage>().WithMany().HasForeignKey(x => x.ExerciseImageId).OnDelete(DeleteBehavior.Restrict); }
}
