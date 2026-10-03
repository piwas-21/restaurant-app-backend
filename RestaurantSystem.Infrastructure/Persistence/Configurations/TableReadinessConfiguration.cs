using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

internal static class TableReadinessConfiguration
{
    internal static void Apply(EntityTypeBuilder<Table> builder)
    {
        builder.ToTable("Tables", table => table.HasCheckConstraint(
            "ck_table_readiness_shape",
            "readiness_version > 0 AND readiness_state IN ('NeedsReset', 'ReadyForGuests')"));
        builder.Property(table => table.ReadinessState)
            .HasConversion<string>().HasMaxLength(24)
            .HasDefaultValue(TableReadinessState.NeedsReset).IsRequired();
        builder.Property(table => table.ReadinessVersion).HasDefaultValue(1).IsRequired();
    }
}
