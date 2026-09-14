using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class TableBillPaymentOperationConfiguration : IEntityTypeConfiguration<TableBillPaymentOperation>
{
    public void Configure(EntityTypeBuilder<TableBillPaymentOperation> builder)
    {
        builder.Property(operation => operation.OperationId).IsRequired();
        builder.Property(operation => operation.ExpectedVersion);
        builder.Property(operation => operation.Currency).HasMaxLength(3);
        builder.Property(operation => operation.Amount).HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(operation => operation.PaymentMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.TransactionId).HasMaxLength(100);
        builder.Property(operation => operation.ReferenceNumber).HasMaxLength(50);
        builder.Property(operation => operation.CardLastFourDigits).HasMaxLength(4);
        builder.Property(operation => operation.CardType).HasMaxLength(20);
        builder.Property(operation => operation.PaymentNotes).HasMaxLength(500);
        builder.HasIndex(operation => operation.OperationId).IsUnique();
        builder.HasIndex(operation => operation.ServiceSessionId);

        builder.HasOne(operation => operation.ServiceSession)
            .WithMany()
            .HasForeignKey(operation => operation.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
