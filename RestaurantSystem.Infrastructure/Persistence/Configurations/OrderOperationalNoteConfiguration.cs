using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderOperationalNoteConfiguration : IEntityTypeConfiguration<OrderOperationalNote>
{
    public void Configure(EntityTypeBuilder<OrderOperationalNote> builder)
    {
        builder.ToTable("OrderOperationalNotes");
        builder.Property(note => note.Text).IsRequired().HasMaxLength(500);
        builder.Property(note => note.Audience).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(note => note.ClientOperationId).IsRequired();

        builder.HasIndex(note => new { note.OrderId, note.CreatedAt });
        // Each tenant has its own database. This key is therefore tenant-scoped while making a retry
        // of the same note operation return exactly one persisted row.
        builder.HasIndex(note => new { note.OrderId, note.ClientOperationId }).IsUnique();

        builder.HasOne(note => note.Order)
            .WithMany(order => order.OperationalNotes)
            .HasForeignKey(note => note.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
