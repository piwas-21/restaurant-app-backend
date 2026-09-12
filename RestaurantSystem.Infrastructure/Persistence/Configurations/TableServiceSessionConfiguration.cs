using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class TableServiceSessionConfiguration : IEntityTypeConfiguration<TableServiceSession>
{
    public void Configure(EntityTypeBuilder<TableServiceSession> builder)
    {
        builder.Property(session => session.Currency)
            .HasMaxLength(3);

        builder.Property(session => session.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(session => session.Version)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(session => session.OpenedAt).IsRequired();

        // A table may have one open visit at a time. Closed visits remain durable for receipts,
        // audit and unambiguous historical membership.
        builder.HasIndex(session => session.TableNumber)
            .IsUnique()
            .HasFilter("\"status\" = 'Open'");

        builder.HasIndex(session => session.Status);
        builder.HasIndex(session => session.OpenedAt);

        builder.HasMany(session => session.Orders)
            .WithOne(order => order.ServiceSession)
            .HasForeignKey(order => order.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
