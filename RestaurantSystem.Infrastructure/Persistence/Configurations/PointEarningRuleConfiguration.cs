using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class PointEarningRuleConfiguration : IEntityTypeConfiguration<PointEarningRule>
{
    public void Configure(EntityTypeBuilder<PointEarningRule> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.MinOrderAmount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.MaxOrderAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.PointsAwarded)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.Priority)
            .IsRequired()
            .HasDefaultValue(0);

        // Indexes
        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => new { p.MinOrderAmount, p.MaxOrderAmount });
        builder.HasIndex(p => p.Priority);
    }
}
