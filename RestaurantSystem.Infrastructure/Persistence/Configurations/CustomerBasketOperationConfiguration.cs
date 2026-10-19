using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerBasketOperationConfiguration : IEntityTypeConfiguration<CustomerBasketOperation>
{
    public void Configure(EntityTypeBuilder<CustomerBasketOperation> builder)
    {
        builder.ToTable("customer_basket_operations");
        builder.HasKey(operation => operation.OperationId).HasName("pk_customer_basket_operations");
        builder.Property(operation => operation.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(operation => operation.OwnerHash).HasColumnName("owner_hash").HasMaxLength(64).IsRequired();
        builder.Property(operation => operation.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        // No basket FK: cleanup must not erase the receipt and make a replay add the item again.
        builder.Property(operation => operation.BasketId).HasColumnName("basket_id").IsRequired();
        builder.Property(operation => operation.IsCancelled).HasColumnName("is_cancelled").HasDefaultValue(false);
        builder.Property(operation => operation.AppliedAt).HasColumnName("applied_at").IsRequired();
    }
}
