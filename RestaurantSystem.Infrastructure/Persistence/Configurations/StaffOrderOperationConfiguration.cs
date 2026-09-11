using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class StaffOrderOperationConfiguration : IEntityTypeConfiguration<StaffOrderOperation>
{
    public void Configure(EntityTypeBuilder<StaffOrderOperation> builder)
    {
        builder.Property(operation => operation.OperationId).IsRequired();
        builder.Property(operation => operation.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(operation => operation.ActorRole).HasMaxLength(30).IsRequired();
        builder.Property(operation => operation.PayloadHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(operation => operation.OperationId).IsUnique();
        builder.HasIndex(operation => operation.OrderId);
        builder.HasOne(operation => operation.Order)
            .WithMany()
            .HasForeignKey(operation => operation.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
