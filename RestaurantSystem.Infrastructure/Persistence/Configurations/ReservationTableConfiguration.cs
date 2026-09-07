using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ReservationTableConfiguration : IEntityTypeConfiguration<ReservationTable>
{
    public void Configure(EntityTypeBuilder<ReservationTable> builder)
    {
        builder.ToTable("ReservationTables");

        // A reservation cannot list the same table twice — the handler refuses it, and the
        // database is the second line of defence.
        builder.HasIndex(rt => new { rt.ReservationId, rt.TableId })
            .IsUnique();

        // The children exist only as part of their reservation.
        builder.HasOne(rt => rt.Reservation)
            .WithMany(r => r.CombinedTables)
            .HasForeignKey(rt => rt.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same posture as Table -> Reservations: table rows own QRs and must survive.
        builder.HasOne(rt => rt.Table)
            .WithMany()
            .HasForeignKey(rt => rt.TableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
