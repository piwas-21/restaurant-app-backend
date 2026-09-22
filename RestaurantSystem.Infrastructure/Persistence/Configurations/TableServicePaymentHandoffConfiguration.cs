using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableServicePaymentHandoffConfiguration : IEntityTypeConfiguration<TableServicePaymentHandoff>
{
    public void Configure(EntityTypeBuilder<TableServicePaymentHandoff> builder)
    {
        builder.Property(handoff => handoff.OperationId).IsRequired();
        builder.Property(handoff => handoff.RequestedAmount)
            .HasColumnType("decimal(10,2)")
            .IsRequired();
        builder.Property(handoff => handoff.RequestedCurrency).HasMaxLength(3);
        builder.Property(handoff => handoff.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(handoff => handoff.RequestedAt).IsRequired();
        builder.Property(handoff => handoff.ResolvedBy).HasMaxLength(200);
        builder.Property(handoff => handoff.CancelledBy).HasMaxLength(200);

        builder.HasIndex(handoff => handoff.OperationId).IsUnique();
        builder.HasIndex(handoff => handoff.ServiceSessionId);
        builder.HasIndex(handoff => handoff.ServiceSessionId)
            .IsUnique()
            .HasFilter("\"status\" = 'Requested'");
        builder.HasIndex(handoff => handoff.CancellationOperationId).IsUnique();
        builder.HasIndex(handoff => new { handoff.Status, handoff.RequestedAt });

        builder.HasOne(handoff => handoff.ServiceSession)
            .WithMany()
            .HasForeignKey(handoff => handoff.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
