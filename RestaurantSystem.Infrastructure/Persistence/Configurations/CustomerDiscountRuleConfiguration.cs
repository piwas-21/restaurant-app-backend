using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class CustomerDiscountRuleConfiguration : IEntityTypeConfiguration<CustomerDiscountRule>
{
    public void Configure(EntityTypeBuilder<CustomerDiscountRule> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId)
            .IsRequired();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.DiscountType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(c => c.DiscountValue)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(c => c.MinOrderAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(c => c.MaxOrderAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(c => c.UsageCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(c => c.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Indexes
        builder.HasIndex(c => c.UserId);
        builder.HasIndex(c => c.IsActive);
        builder.HasIndex(c => new { c.ValidFrom, c.ValidUntil });

        // Relationships
        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
