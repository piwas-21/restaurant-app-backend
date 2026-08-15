using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.OrderNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(o => o.OrderNumber)
            .IsUnique();

        // 32 random bytes, base64url-encoded => 43 chars. Capped well above that so a future
        // widening is a config change rather than a data migration.
        builder.Property(o => o.QuickActionToken)
            .HasMaxLength(64);

        // Unique so a generator regression that emitted a constant or repeated token fails the
        // INSERT instead of silently making one link open several orders. Postgres treats NULLs
        // as distinct, so the pre-column rows (all null) do not collide with each other.
        builder.HasIndex(o => o.QuickActionToken)
            .IsUnique();

        builder.Property(o => o.CustomerName)
            .HasMaxLength(100);

        builder.Property(o => o.CustomerEmail)
            .HasMaxLength(100);

        builder.Property(o => o.CustomerPhone)
            .HasMaxLength(20);

        // See ApplicationUserConfiguration for why the whitelist is enforced at the persistence
        // boundary and not only on the write path.
        builder.Property(o => o.PreferredLanguage)
            .HasMaxLength(LanguageCode.MaxLength)
            .HasConversion(value => LanguageCode.Normalize(value), stored => stored);

        builder.Property(o => o.SubTotal)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.Tax)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.DeliveryFee)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.Discount)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.DiscountPercentage)
            .HasColumnType("decimal(5,2)");

        builder.Property(o => o.Tip)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.Total)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.UserLimitAmount)
            .HasColumnType("decimal(10,2)");

        builder.Property(o => o.PromoCode)
            .HasMaxLength(50);

        builder.Property(o => o.Notes)
            .HasMaxLength(1000);

        builder.Property(o => o.CancellationReason)
            .HasMaxLength(500);

        builder.Property(o => o.Type)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Indexes
        builder.HasIndex(o => o.UserId);
        builder.HasIndex(o => o.OrderDate);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => new { o.UserId, o.OrderDate });

        // Relationships
        builder.HasOne(o => o.User)
            .WithMany()
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.StatusHistory)
            .WithOne(h => h.Order)
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(o => o.TotalPaid)
        .HasColumnType("decimal(10,2)");

        builder.Property(o => o.RemainingAmount)
            .HasColumnType("decimal(10,2)");

        // Focus shares the Orders row rather than getting a table of its own: it is read on every
        // order fetch, so a join would cost more than the five columns it saves. Column names are
        // pinned to the pre-extraction ones, which keeps this a code move — the only schema change
        // is dropping IsFocusOrder, whose truth now lives in whether FocusedAt is NULL.
        // ToTable and the snake_case names are spelled out rather than left to convention:
        // ApplicationDbContext.ConfigurePostgreSQL runs *before* ApplyConfigurationsFromAssembly,
        // so nothing this file introduces is reached by the snake_case pass, and an owned type left
        // to its own devices here scaffolds a separate order_focus table with PascalCase columns.
        builder.OwnsOne(o => o.Focus, focus =>
        {
            focus.ToTable("orders");

            focus.Property(f => f.Priority)
                .HasColumnName("priority");

            focus.Property(f => f.Reason)
                .HasColumnName("focus_reason")
                .HasMaxLength(500);

            focus.Property(f => f.FocusedAt)
                .HasColumnName("focused_at");

            focus.Property(f => f.FocusedBy)
                .HasColumnName("focused_by")
                .HasMaxLength(100);

            // Replaces HasIndex(IsFocusOrder) and HasIndex(IsFocusOrder, Priority). Focused orders
            // are a small slice of a table that only grows, so a partial index stays proportional to
            // the slice instead of the table, and it covers GetFocusOrders exactly: filter on
            // focused, then order by priority, then by focused_at.
            focus.HasIndex(f => new { f.Priority, f.FocusedAt })
                .HasDatabaseName("IX_orders_priority_focused_at")
                .HasFilter("\"focused_at\" IS NOT NULL");
        });

        // Owned references that share the owner's table are required by default; this one is the
        // whole point of the extraction, so it has to be optional.
        builder.Navigation(o => o.Focus)
            .IsRequired(false);
    }
}
