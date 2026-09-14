using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderChangeConfiguration : IEntityTypeConfiguration<OrderChange>
{
    public void Configure(EntityTypeBuilder<OrderChange> builder)
    {
        builder.ToTable("order_changes");
        builder.HasKey(change => change.Sequence);
        // PostgreSQL allocates sequence values in the trigger. Keeping this generated means direct
        // EF inserts (fixtures/imports) use the same monotonic tenant-database sequence.
        builder.Property(change => change.Sequence)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("next_order_queue_sequence()");
        builder.Property(change => change.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(change => change.Reason).HasMaxLength(100);

        builder.HasIndex(change => change.OrderId);
        builder.HasOne(change => change.Order)
            .WithMany()
            .HasForeignKey(change => change.OrderId)
            // Removal entries must remain durable. A hard-delete is therefore refused while a
            // journal row points at the order; normal order deletion is a soft delete.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
