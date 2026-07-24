using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class FloorPlanOpeningConfiguration : IEntityTypeConfiguration<FloorPlanOpening>
{
    public void Configure(EntityTypeBuilder<FloorPlanOpening> builder)
    {
        builder.ToTable("FloorPlanOpenings");

        builder.Property(o => o.OffsetMeters).HasColumnType("decimal(6,2)");
        builder.Property(o => o.WidthMeters).HasColumnType("decimal(6,2)");
        builder.Property(o => o.Kind).IsRequired().HasMaxLength(20);
        builder.Property(o => o.SwingDirection).IsRequired().HasMaxLength(20).HasDefaultValue("none");

        builder.HasIndex(o => o.WallId);
    }
}
