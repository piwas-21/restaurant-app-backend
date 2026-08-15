using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        builder.Property(r => r.CustomerName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.CustomerEmail)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(r => r.CustomerPhone)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(r => r.ReservationDate)
            .IsRequired();

        builder.Property(r => r.StartTime)
            .IsRequired();

        builder.Property(r => r.EndTime)
            .IsRequired();

        builder.Property(r => r.NumberOfGuests)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<int>();

        builder.Property(r => r.SpecialRequests)
            .HasMaxLength(1000);

        builder.Property(r => r.Notes)
            .HasMaxLength(1000);

        // Safety net for the STORED value, with the same two limits spelled out in
        // ApplicationUserConfiguration: it rewrites the SQL parameter and not the object in
        // memory, and it is EF-scoped. S4 still resolves and assigns a canonical code.
        builder.Property(r => r.PreferredLanguage)
            .HasMaxLength(LanguageCode.MaxLength)
            .HasConversion(value => LanguageCode.Normalize(value), stored => stored);

        // Create indexes for common queries
        builder.HasIndex(r => r.ReservationDate);
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => new { r.TableId, r.ReservationDate });
        builder.HasIndex(r => r.CustomerId);

        // Configure relationship with Table
        builder.HasOne(r => r.Table)
            .WithMany(t => t.Reservations)
            .HasForeignKey(r => r.TableId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure relationship with Customer (optional, for registered users)
        builder.HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
