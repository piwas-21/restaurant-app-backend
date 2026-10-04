using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentResolutionOperationConfiguration
    : IEntityTypeConfiguration<OrderAmendmentResolutionOperation>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentResolutionOperation> builder)
    {
        builder.ToTable("order_amendment_resolution_operations", table => table.HasCheckConstraint(
            "ck_amendment_resolution_operation_amounts",
            "credit_minor > 0 AND refund_minor >= 0 AND unpaid_waived_minor >= 0"
            + " AND refund_minor + unpaid_waived_minor = credit_minor"));
        builder.Property(value => value.ActorRole).HasMaxLength(30).IsRequired();
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(value => value.ResultJson).HasColumnType("jsonb");
        builder.Property(value => value.State).HasConversion<string>().HasMaxLength(30);
        builder.Property(value => value.FailureCode).HasMaxLength(80);
        builder.HasIndex(value => new { value.ActorUserId, value.ClientOperationId }).IsUnique();
        builder.HasIndex(value => value.AmendmentId).IsUnique();
        builder.HasIndex(value => new { value.ServiceSessionId, value.State });
        builder.HasOne<OrderAmendment>().WithMany().HasForeignKey(value => value.AmendmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.SourceOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TableServiceSession>().WithMany().HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
