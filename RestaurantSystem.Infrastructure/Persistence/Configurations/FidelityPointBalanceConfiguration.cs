using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class FidelityPointBalanceConfiguration : IEntityTypeConfiguration<FidelityPointBalance>
{
    public void Configure(EntityTypeBuilder<FidelityPointBalance> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.UserId)
            .IsRequired();

        builder.Property(f => f.CurrentPoints)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(f => f.TotalEarnedPoints)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(f => f.TotalRedeemedPoints)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(f => f.LastUpdated)
            .IsRequired();

        // Unique constraint on UserId (one balance per user)
        builder.HasIndex(f => f.UserId)
            .IsUnique();

        // Relationships
        builder.HasOne(f => f.User)
            .WithOne()
            .HasForeignKey<FidelityPointBalance>(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
