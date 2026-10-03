using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentResolutionRefusalConfiguration
    : IEntityTypeConfiguration<OrderAmendmentResolutionRefusal>
{
    public void Configure(EntityTypeBuilder<OrderAmendmentResolutionRefusal> builder)
    {
        builder.ToTable("order_amendment_resolution_refusals", table =>
        {
            table.HasCheckConstraint("ck_amendment_resolution_refusal_hash", "request_hash ~ '^[a-f0-9]{64}$'");
            table.HasCheckConstraint("ck_amendment_resolution_refusal_code",
                "failure_code IN ('quoteExpired','sourceVersionConflict','accountRevisionConflict',"
                + "'quoteChanged')");
        });
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.FailureCode).HasMaxLength(40).IsRequired();
        builder.Property(value => value.OriginalRequestJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(value => new { value.ActorUserId, value.ClientOperationId }).IsUnique();
        builder.HasIndex(value => new { value.ActorUserId, value.SourceOrderId, value.AmendmentId });
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.SourceOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderAmendment>().WithMany().HasForeignKey(value => value.AmendmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
