using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerReservationOperationConfiguration : IEntityTypeConfiguration<CustomerReservationOperation>
{
    public void Configure(EntityTypeBuilder<CustomerReservationOperation> builder)
    {
        builder.ToTable("customer_reservation_operations", table => table.HasCheckConstraint(
            "ck_customer_reservation_operations_outcome",
            "(is_cancelled AND reservation_id IS NULL) OR (NOT is_cancelled AND reservation_id IS NOT NULL AND reservation_id <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        builder.HasKey(row => row.OperationId).HasName("pk_customer_reservation_operations");
        builder.Property(row => row.OperationId).HasColumnName("operation_id").ValueGeneratedNever();
        builder.Property(row => row.OwnerHash).HasColumnName("owner_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        // No booking/user FK: erasure and deletion must not turn an accepted attempt into a new write.
        builder.Property(row => row.ReservationId).HasColumnName("reservation_id");
        builder.Property(row => row.IsCancelled).HasColumnName("is_cancelled").HasDefaultValue(false);
        builder.Property(row => row.CompletedAt).HasColumnName("completed_at").IsRequired();
    }
}
