using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CustomerNotificationWatchConfiguration : IEntityTypeConfiguration<CustomerNotificationWatch>
{
    public void Configure(EntityTypeBuilder<CustomerNotificationWatch> builder)
    {
        builder.ToTable("customer_notification_watches", table => table.HasCheckConstraint(
            "ck_customer_notification_watches_subject",
            "subject_kind IN ('order', 'reservation') AND subject_id <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(row => new { row.SubjectKind, row.SubjectId });
        builder.Property(row => row.SubjectKind).HasColumnName("subject_kind").HasMaxLength(16);
        builder.Property(row => row.SubjectId).HasColumnName("subject_id").ValueGeneratedNever();
    }
}
