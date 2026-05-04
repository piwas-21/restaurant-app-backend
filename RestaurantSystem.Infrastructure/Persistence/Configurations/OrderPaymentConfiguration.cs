using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> builder)
    {
        builder.Property(p => p.Amount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();

        builder.Property(p => p.RefundedAmount)
            .HasColumnType("decimal(10,2)");

        builder.Property(p => p.PaymentMethod)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.TransactionId)
            .HasMaxLength(100);

        builder.Property(p => p.ReferenceNumber)
            .HasMaxLength(50);

        builder.Property(p => p.CardLastFourDigits)
            .HasMaxLength(4);

        builder.Property(p => p.CardType)
            .HasMaxLength(20);

        builder.Property(p => p.PaymentGateway)
            .HasMaxLength(50);

        builder.Property(p => p.PaymentNotes)
            .HasMaxLength(500);

        builder.Property(p => p.RefundReason)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.TransactionId);
        builder.HasIndex(p => p.PaymentDate);

        // Relationships
        builder.HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
