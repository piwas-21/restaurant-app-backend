using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class ExternalOrderReferenceConfiguration : IEntityTypeConfiguration<ExternalOrderReference>
{
    public void Configure(EntityTypeBuilder<ExternalOrderReference> builder)
    {
        builder.ToTable("ExternalOrderReferences", table =>
        {
            table.HasCheckConstraint("CK_ExternalOrderReferences_Currency", "\"currency\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_ExternalOrderReferences_PayloadHash", "\"payload_hash\" ~ '^[a-f0-9]{64}$'");
            table.HasCheckConstraint("CK_ExternalOrderReferences_Money",
                "\"merchant_total\" >= 0 AND (\"reported_tax\" IS NULL OR \"reported_tax\" >= 0)");
        });
        builder.Property(reference => reference.Provider).IsRequired().HasMaxLength(50);
        builder.Property(reference => reference.ExternalStoreId).IsRequired().HasMaxLength(200);
        builder.Property(reference => reference.ExternalOrderId).IsRequired().HasMaxLength(200);
        builder.Property(reference => reference.ExternalDisplayId).IsRequired().HasMaxLength(100);
        builder.Property(reference => reference.ExternalState).IsRequired().HasMaxLength(64);
        builder.Property(reference => reference.Currency).IsRequired().HasMaxLength(3);
        builder.Property(reference => reference.PayloadHash).IsRequired().HasMaxLength(64);
        builder.Property(reference => reference.CanonicalHash).HasMaxLength(64);
        builder.Property(reference => reference.FulfillmentType).IsRequired().HasMaxLength(64);
        builder.Property(reference => reference.MerchantTotal).HasPrecision(10, 2);
        builder.Property(reference => reference.ReportedTax).HasPrecision(10, 2);
        builder.HasIndex(reference => new { reference.Provider, reference.ExternalStoreId, reference.ExternalOrderId })
            .IsUnique();
        builder.HasOne(reference => reference.Order).WithOne(order => order.ExternalReference)
            .HasForeignKey<ExternalOrderReference>(reference => reference.OrderId).OnDelete(DeleteBehavior.ClientNoAction);
        // Orders.Remove is converted into a soft delete at save time. EF must not physically
        // delete the included identity first and allow that provider key to be imported again.
    }
}
