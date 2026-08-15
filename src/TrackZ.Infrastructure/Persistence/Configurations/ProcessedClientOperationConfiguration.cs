using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrackZ.Domain.Identity;
using TrackZ.Domain.Sync;

namespace TrackZ.Infrastructure.Persistence.Configurations;

public sealed class ProcessedClientOperationConfiguration
    : IEntityTypeConfiguration<ProcessedClientOperation>
{
    public void Configure(EntityTypeBuilder<ProcessedClientOperation> builder)
    {
        builder.ToTable("processed_client_operations", table =>
        {
            table.HasCheckConstraint(
                "CK_processed_client_operations_fingerprint",
                "length(\"RequestFingerprint\") = 64");
            table.HasCheckConstraint(
                "CK_processed_client_operations_result",
                "jsonb_typeof(\"ResultJson\") = 'object'");
        });
        builder.HasKey(operation => new { operation.UserId, operation.OperationId });
        builder.Property(operation => operation.UserId).ValueGeneratedNever();
        builder.Property(operation => operation.OperationId).ValueGeneratedNever();
        builder.Property(operation => operation.RequestFingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(operation => operation.ResultJson).HasColumnType("jsonb").IsRequired();
        builder.Property(operation => operation.ProcessedAt).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(operation => operation.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(operation => operation.ProcessedAt);
    }
}
