using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableGuestAdmissionConfiguration : IEntityTypeConfiguration<TableGuestAdmission>
{
    public void Configure(EntityTypeBuilder<TableGuestAdmission> builder)
    {
        builder.Property(value => value.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(value => value.ExpiresAt).IsRequired();
        builder.HasAlternateKey(value => new { value.Id, value.ServiceSessionId });
        builder.HasIndex(value => value.ServiceSessionId)
            .IsUnique()
            .HasFilter("\"revoked_at\" IS NULL");
        builder.HasOne(value => value.ServiceSession)
            .WithMany()
            .HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
