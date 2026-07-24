using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class FloorPlanWallConfiguration : IEntityTypeConfiguration<FloorPlanWall>
{
    public void Configure(EntityTypeBuilder<FloorPlanWall> builder)
    {
        builder.ToTable("FloorPlanWalls");

        // Vertices as a jsonb array of { x, y } metres. Stored as text mapped to
        // jsonb so the whole-document PUT round-trips one value (no owned-type
        // change tracking); the handler validates/caps vertex count.
        builder.Property(w => w.PointsJson).IsRequired().HasColumnType("jsonb");
        builder.Property(w => w.ThicknessMeters).HasColumnType("decimal(5,2)").HasDefaultValue(0.12m);
        builder.Property(w => w.IsClosed).HasDefaultValue(false);
        builder.Property(w => w.RoomName).HasMaxLength(80);
        builder.Property(w => w.FloorStyle).HasMaxLength(40);

        builder.HasIndex(w => w.FloorPlanId);

        builder.HasMany(w => w.Openings)
            .WithOne(o => o.Wall)
            .HasForeignKey(o => o.WallId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
