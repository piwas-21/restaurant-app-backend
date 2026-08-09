using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderCheckoutSessionConfiguration : IEntityTypeConfiguration<OrderCheckoutSession>
{
    public void Configure(EntityTypeBuilder<OrderCheckoutSession> builder)
    {
        // UNIQUE, and load-bearing rather than tidy: settlement claims a row by SessionId, and both
        // callers (the success_url return trip and the reconciler) can arrive at once. The database
        // is what makes "settle exactly once" true — not the handler's read-then-write.
        builder.HasIndex(s => s.SessionId).IsUnique();

        builder.Property(s => s.SessionId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.PaymentIntentId)
            .HasMaxLength(255);

        // Stored as its NAME, matching every other enum in this schema (see OrderPaymentConfiguration).
        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(s => s.IdempotencyKey)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.ConnectedAccountId)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(s => s.LastError)
            .HasMaxLength(500);

        // The reconciler's sweep is "still Created and past ExpiresAt", so it reads on exactly these
        // two columns; without the index it table-scans every session ever created on a timer.
        builder.HasIndex(s => new { s.Status, s.ExpiresAt });

        builder.HasOne(s => s.Order)
            .WithMany()
            .HasForeignKey(s => s.OrderId)
            // An order carrying an unsettled Stripe session must not be deletable out from under it —
            // the row is the only local record that money may be in flight.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
