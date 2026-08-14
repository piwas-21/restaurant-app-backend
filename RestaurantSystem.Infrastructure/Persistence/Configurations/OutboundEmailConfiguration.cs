using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OutboundEmailConfiguration : IEntityTypeConfiguration<OutboundEmail>
{
    public void Configure(EntityTypeBuilder<OutboundEmail> builder)
    {
        // Load-bearing, not tidy: this index IS the "send at most once" rule. The server sends the
        // order mail the moment the order commits, and the guest's browser may ask for the same
        // mail at the same instant through the legacy endpoint; only the database can arbitrate
        // between two callers that both read "not sent yet".
        builder.HasIndex(e => new { e.EmailType, e.EntityId }).IsUnique();

        builder.Property(e => e.EmailType)
            .HasMaxLength(64)
            .IsRequired();

        // No FK to Orders on purpose: the same table guards reservation and account mail next
        // (GAP-18), and a claim must survive the row it refers to being purged — a GDPR erasure
        // must not resurrect a mail that was already sent.
    }
}
