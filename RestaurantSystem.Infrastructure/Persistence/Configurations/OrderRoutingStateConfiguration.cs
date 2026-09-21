using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class OrderRoutingStateConfiguration : IEntityTypeConfiguration<OrderRoutingState>
{
    public void Configure(EntityTypeBuilder<OrderRoutingState> builder)
    {
        builder.ToTable("OrderRoutingStates");
        builder.Property(state => state.Target).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(state => state.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(state => state.Version).IsConcurrencyToken();
        builder.Property(state => state.DeviceId).HasMaxLength(64);
        builder.Property(state => state.FailureReason).HasMaxLength(500);
        builder.HasOne(state => state.Order)
            .WithMany(order => order.RoutingStates)
            .HasForeignKey(state => state.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(state => new { state.JobId, state.Revision, state.Target }).IsUnique();
        builder.HasIndex(state => new { state.OrderId, state.CreatedAt });
    }
}
