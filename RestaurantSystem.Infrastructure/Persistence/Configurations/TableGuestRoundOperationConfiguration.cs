using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class TableGuestRoundOperationConfiguration : IEntityTypeConfiguration<TableGuestRoundOperation>
{
    public void Configure(EntityTypeBuilder<TableGuestRoundOperation> builder)
    {
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(value => new { value.ServiceSessionId, value.OperationId }).IsUnique();
        builder.HasOne(value => value.Participant)
            .WithMany()
            .HasForeignKey(value => new { value.ParticipantId, value.ServiceSessionId })
            .HasPrincipalKey(participant => new { participant.Id, participant.ServiceSessionId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.ServiceSession)
            .WithMany()
            .HasForeignKey(value => value.ServiceSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Order)
            .WithMany()
            .HasForeignKey(value => value.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
