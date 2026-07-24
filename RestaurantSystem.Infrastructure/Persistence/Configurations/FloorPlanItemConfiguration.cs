using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class FloorPlanItemConfiguration : IEntityTypeConfiguration<FloorPlanItem>
{
    public void Configure(EntityTypeBuilder<FloorPlanItem> builder)
    {
        builder.ToTable("FloorPlanItems");

        builder.Property(i => i.Kind).IsRequired().HasMaxLength(40);
        builder.Property(i => i.X).HasColumnType("decimal(6,2)");
        builder.Property(i => i.Y).HasColumnType("decimal(6,2)");
        builder.Property(i => i.WidthMeters).HasColumnType("decimal(6,2)");
        builder.Property(i => i.HeightMeters).HasColumnType("decimal(6,2)");
        builder.Property(i => i.RotationDegrees).HasColumnType("decimal(5,1)");
        builder.Property(i => i.Label).HasMaxLength(120);
        builder.Property(i => i.StyleVariant).HasMaxLength(40);

        builder.HasIndex(i => i.FloorPlanId);
    }
}
