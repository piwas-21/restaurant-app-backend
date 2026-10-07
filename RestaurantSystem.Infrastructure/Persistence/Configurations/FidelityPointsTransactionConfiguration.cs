using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class FidelityPointsTransactionConfiguration : IEntityTypeConfiguration<FidelityPointsTransaction>
{
    public void Configure(EntityTypeBuilder<FidelityPointsTransaction> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.UserId)
            .IsRequired(false);

        builder.Property(f => f.TransactionType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(f => f.Points)
            .IsRequired();

        builder.Property(f => f.OrderTotal)
            .HasColumnType("decimal(18,2)");

        builder.Property(f => f.Description)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(f => f.UserId);
        builder.HasIndex(f => f.OrderId);
        builder.HasIndex(f => f.CreatedAt);
        builder.HasIndex(f => f.TransactionType);
        builder.HasIndex(f => f.OriginalTransactionId)
            .HasDatabaseName("ix_fidelity_points_transactions_original_transaction_id");

        // Relationships
        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(f => f.Order)
            .WithMany(o => o.FidelityPointsTransactions)
            .HasForeignKey(f => f.OrderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
