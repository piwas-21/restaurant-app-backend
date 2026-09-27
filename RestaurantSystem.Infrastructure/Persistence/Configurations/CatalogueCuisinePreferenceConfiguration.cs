using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public sealed class CatalogueCuisinePreferenceConfiguration : IEntityTypeConfiguration<CatalogueCuisinePreference>
{
    public void Configure(EntityTypeBuilder<CatalogueCuisinePreference> builder)
    {
        builder.Property(x => x.Cuisines).HasColumnType("text[]").IsRequired();
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_catalogue_cuisine_preferences_singleton", $"id = '{CatalogueCuisinePreference.SingletonId}'"));
    }
}
