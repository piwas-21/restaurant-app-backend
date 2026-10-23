using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerNotificationEventConfiguration : IEntityTypeConfiguration<CustomerNotificationEvent>
{
    public void Configure(EntityTypeBuilder<CustomerNotificationEvent> builder)
    {
        builder.ToTable("customer_notification_events");
        builder.HasKey(row => row.EventId);
        builder.Property(row => row.EventId).HasColumnName("event_id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(row => row.SubjectKind).HasColumnName("subject_kind").HasMaxLength(16);
        builder.Property(row => row.SubjectId).HasColumnName("subject_id").ValueGeneratedNever();
        builder.Property(row => row.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("clock_timestamp()");
        builder.HasIndex(row => new { row.OccurredAt, row.EventId });
        // Withdrawal erases pending hints. No contact, device token, credential or financial record.
        builder.HasOne<CustomerNotificationWatch>().WithMany()
            .HasForeignKey(row => new { row.SubjectKind, row.SubjectId }).OnDelete(DeleteBehavior.Cascade);
    }
}
