using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class ChannelOrderDecisionConfiguration : IEntityTypeConfiguration<ChannelOrderDecision>
{
    public void Configure(EntityTypeBuilder<ChannelOrderDecision> builder)
    {
        builder.ToTable("ChannelOrderDecisions", table =>
        {
            table.HasCheckConstraint("CK_ChannelOrderDecisions_Action", "\"action\" IN ('accept', 'deny')");
            table.HasCheckConstraint("CK_ChannelOrderDecisions_State", "\"state\" IN ('Pending', 'Leased', 'Unknown', 'Succeeded', 'Failed')");
            table.HasCheckConstraint("CK_ChannelOrderDecisions_Hash", "\"payload_hash\" ~ '^[a-f0-9]{64}$'");
        });
        builder.Property(job => job.Action).HasMaxLength(8).IsRequired();
        builder.Property(job => job.Reason).HasMaxLength(250).IsRequired();
        builder.Property(job => job.State).HasMaxLength(20).IsRequired();
        builder.Property(job => job.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(job => job.LastCanonicalHash).HasMaxLength(64);
        builder.Property(job => job.LastReportHash).HasMaxLength(64);
        builder.Property(job => job.ActorRole).HasMaxLength(30).IsRequired();
        builder.HasIndex(job => job.OperationId).IsUnique();
        // One initial decision per order prevents an accept/deny race. Later fulfillment is separate.
        builder.HasIndex(job => job.OrderId).IsUnique();
        builder.HasIndex(job => new { job.State, job.AvailableAt });
        builder.HasOne(job => job.Order).WithMany().HasForeignKey(job => job.OrderId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}
