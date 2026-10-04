using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class OrderOperationalNoteConfiguration : IEntityTypeConfiguration<OrderOperationalNote>
{
    public void Configure(EntityTypeBuilder<OrderOperationalNote> builder)
    {
        builder.ToTable("OrderOperationalNotes", table => table.HasCheckConstraint(
            "ck_order_operational_notes_kitchen_change",
            "(kitchen_changes_json IS NULL AND amendment_id IS NULL AND kitchen_target IS NULL AND account_revision IS NULL) "
            + "OR (kitchen_changes_json IS NOT NULL AND amendment_id IS NOT NULL AND kitchen_target IS NOT NULL "
            + "AND audience = 'Kitchen' AND (account_revision IS NULL OR account_revision > 0))"));
        builder.Property(note => note.Text).IsRequired().HasMaxLength(500);
        builder.Property(note => note.Audience).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(note => note.ClientOperationId).IsRequired();
        builder.Property(note => note.KitchenTarget).HasConversion<string>().HasMaxLength(20);
        builder.Property(note => note.KitchenChangesJson).HasColumnType("jsonb");
        builder.Property<DateTime>("FeedEventAt")
            .HasComputedColumnSql("COALESCE(withdrawn_at, created_at)", stored: true);
        builder.HasIndex("FeedEventAt", nameof(OrderOperationalNote.Id));

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
