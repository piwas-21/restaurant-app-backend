using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderAmendmentConfiguration : IEntityTypeConfiguration<OrderAmendment>
{
    public void Configure(EntityTypeBuilder<OrderAmendment> builder)
    {
        builder.ToTable("order_amendments");
        builder.Property(amendment => amendment.ActorRole).HasMaxLength(30).IsRequired();
        builder.Property(amendment => amendment.State).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(amendment => amendment.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(amendment => amendment.CommitPayloadHash).HasMaxLength(64);
        builder.Property(amendment => amendment.RequestJson).HasColumnType("jsonb").IsRequired();
        builder.Property(amendment => amendment.ChangesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(amendment => amendment.SourceSnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(amendment => amendment.SupplementSnapshotJson).HasColumnType("jsonb");
        builder.Property(amendment => amendment.FinancialResolutionJson).HasColumnType("jsonb").IsRequired();
        builder.Property(amendment => amendment.CommitResultJson).HasColumnType("jsonb");

        builder.HasIndex(amendment => new { amendment.SourceOrderId, amendment.CreatedAt });
        builder.HasIndex(amendment => new { amendment.ServiceSessionId, amendment.State });
        builder.HasIndex(amendment => new { amendment.ActorUserId, amendment.ClientOperationId })
            .IsUnique()
            .HasFilter("\"client_operation_id\" IS NOT NULL");

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(amendment => amendment.SourceOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(amendment => amendment.SupplementOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TableServiceSession>()
            .WithMany()
            .HasForeignKey(amendment => amendment.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
