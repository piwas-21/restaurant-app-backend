using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class MenuDefinitionConfiguration : IEntityTypeConfiguration<MenuDefinition>
{
    public void Configure(EntityTypeBuilder<MenuDefinition> builder)
    {
        builder.HasOne(m => m.Product)
            .WithOne(p => p.MenuDefinition)
            .HasForeignKey<MenuDefinition>(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(m => m.StartTime)
            .HasColumnType("time");

        builder.Property(m => m.EndTime)
            .HasColumnType("time");
    }
}
