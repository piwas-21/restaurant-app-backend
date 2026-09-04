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

        // NOT IsRequired, and the mapping is what was wrong rather than the entity or the column.
        // `Reservation.CustomerPhone` is `string?`, and migration `MakeCustomerPhoneOptional`
        // (20251102031347) is the only migration to touch this column after the table was created —
        // it made it NULLABLE. The mapping was never brought into line, so the model believed a
        // nullable column was non-nullable and materialised it with a non-null read: one NULL row
        // threw `InvalidCastException`, `GetReservationsQueryHandler` caught it, and the whole page
        // answered 200 / success:false. ONE phoneless booking hid EVERY reservation.
        //
        // The in-query `?? string.Empty` does not save it — measured, not assumed: the projection's
        // SQL COALESCE is not where the failure happens.
        //
        // Requiredness of a phone is a per-tenant admin setting enforced at WRITE time
        // (`EnsureRequiredFieldsPresentAsync`), which is the right place for a policy that differs
        // by restaurant. It is not a schema invariant, and pretending it was here is what broke the
        // read.
        builder.Property(r => r.CustomerPhone)
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
