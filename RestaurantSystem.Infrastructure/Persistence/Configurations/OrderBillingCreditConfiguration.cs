using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderBillingCreditConfiguration : IEntityTypeConfiguration<OrderBillingCredit>
{
    public void Configure(EntityTypeBuilder<OrderBillingCredit> builder)
    {
        builder.ToTable("order_billing_credits", table => table.HasCheckConstraint(
            "ck_order_billing_credit_money", "amount_minor > 0 AND currency ~ '^[A-Z]{3}$'"));
        builder.Property(value => value.Currency).HasMaxLength(3).IsRequired();
        builder.Property(value => value.ActorRole).HasMaxLength(30).IsRequired();
        builder.HasIndex(value => value.AmendmentId).IsUnique();
        builder.HasIndex(value => new { value.SourceOrderId, value.CreatedAt });
        builder.HasOne<Order>().WithMany().HasForeignKey(value => value.SourceOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderAmendment>().WithMany()
            .HasForeignKey(value => new { value.AmendmentId, value.SourceOrderId })
            .HasPrincipalKey(value => new { value.Id, value.SourceOrderId })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
