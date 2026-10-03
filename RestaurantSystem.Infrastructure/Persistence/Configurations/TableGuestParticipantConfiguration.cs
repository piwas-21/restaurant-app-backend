using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableGuestParticipantConfiguration : IEntityTypeConfiguration<TableGuestParticipant>
{
    public void Configure(EntityTypeBuilder<TableGuestParticipant> builder)
    {
        builder.Property(value => value.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(value => value.ExpiresAt).IsRequired();
        builder.HasIndex(value => value.TokenHash).IsUnique();
        builder.HasIndex(value => new { value.ServiceSessionId, value.RevokedAt });
        builder.HasAlternateKey(value => new { value.Id, value.ServiceSessionId });
        builder.HasOne(value => value.ServiceSession)
            .WithMany()
            .HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Admission)
            .WithMany(admission => admission.Participants)
            .HasForeignKey(value => new { value.AdmissionId, value.ServiceSessionId })
            .HasPrincipalKey(admission => new { admission.Id, admission.ServiceSessionId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
