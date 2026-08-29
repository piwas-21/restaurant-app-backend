using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class WorkingHoursShiftConfiguration : IEntityTypeConfiguration<WorkingHoursShift>
{
    public void Configure(EntityTypeBuilder<WorkingHoursShift> builder)
    {
        builder.HasKey(s => s.Id);

        // Cascade, deliberately: a shift has no meaning without its day, and the day rows are
        // created once by the seeder and never deleted. The alternative (restrict) would only
        // surface as a foreign-key error in a path nobody exercises.
        builder.HasOne(s => s.WorkingHours)
            .WithMany(wh => wh.Shifts)
            .HasForeignKey(s => s.WorkingHoursId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every read is "the shifts of this day, in time order".
        builder.HasIndex(s => new { s.WorkingHoursId, s.OpenTime });
    }
}
