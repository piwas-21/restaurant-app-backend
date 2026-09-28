using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("order_number_sequences", table =>
            table.HasCheckConstraint("ck_order_number_sequences_last_sequence", "last_sequence >= 0"));

        builder.HasKey(sequence => sequence.Day)
            .HasName("pk_order_number_sequences");

        builder.Property(sequence => sequence.Day)
            .HasColumnName("day")
            .HasColumnType("date");

        builder.Property(sequence => sequence.LastSequence)
            .HasColumnName("last_sequence")
            .IsRequired();
    }
}
