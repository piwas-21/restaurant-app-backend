using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class TableConfiguration : IEntityTypeConfiguration<Table>
{
    public void Configure(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("Tables");

        builder.Property(t => t.TableNumber)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(t => t.MaxGuests)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true);

        builder.Property(t => t.IsOutdoor)
            .HasDefaultValue(false);

        builder.Property(t => t.PositionX)
            .HasColumnType("decimal(10,2)");

        builder.Property(t => t.PositionY)
            .HasColumnType("decimal(10,2)");

        builder.Property(t => t.Width)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(80);

        builder.Property(t => t.Height)
            .HasColumnType("decimal(10,2)")
            .HasDefaultValue(80);

        // Create index for quick lookup by table number
        builder.HasIndex(t => t.TableNumber)
            .IsUnique();

        // Configure relationship with Reservations
        builder.HasMany(t => t.Reservations)
            .WithOne(r => r.Table)
            .HasForeignKey(r => r.TableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
