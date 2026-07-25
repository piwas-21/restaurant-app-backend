using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class FloorPlanConfiguration : IEntityTypeConfiguration<FloorPlan>
{
    public void Configure(EntityTypeBuilder<FloorPlan> builder)
    {
        builder.ToTable("FloorPlans");

        builder.Property(p => p.Name).IsRequired().HasMaxLength(120);
        builder.Property(p => p.WidthMeters).HasColumnType("decimal(6,2)");
        builder.Property(p => p.HeightMeters).HasColumnType("decimal(6,2)");
        builder.Property(p => p.GridSizeCm).HasDefaultValue(25);
        builder.Property(p => p.BackgroundStyle).IsRequired().HasMaxLength(40).HasDefaultValue("plain");
        builder.Property(p => p.IsDefault).HasDefaultValue(false);

        // The guest map loads the single default plan — index the lookup.
        builder.HasIndex(p => p.IsDefault);

        builder.HasMany(p => p.Walls)
            .WithOne(w => w.FloorPlan)
            .HasForeignKey(w => w.FloorPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Items)
            .WithOne(i => i.FloorPlan)
            .HasForeignKey(i => i.FloorPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
