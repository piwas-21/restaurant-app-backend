using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class MenuSectionConfiguration : IEntityTypeConfiguration<MenuSection>
{
    public void Configure(EntityTypeBuilder<MenuSection> builder)
    {
        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasOne(s => s.MenuDefinition)
            .WithMany(m => m.Sections)
            .HasForeignKey(s => s.MenuDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
