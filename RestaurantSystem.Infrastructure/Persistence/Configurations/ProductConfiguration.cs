using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {

        builder.ToTable("Products");

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.BasePrice)
            .HasColumnType("decimal(10,2)");

        builder.Property(p => p.ImageUrl)
            .HasMaxLength(2048);

        builder.Property(p => p.Ingredients)
            .HasColumnType("jsonb")
            .HasConversion<List<string>>(); ;

        builder.Property(p => p.Allergens)
            .HasColumnType("jsonb")
            .HasConversion<List<string>>();

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true);

        builder.Property(p => p.IsSpecial)
            .HasDefaultValue(false);

        builder.Property(p => p.PreparationTimeMinutes)
            .IsRequired();

        builder.Property(p => p.Type)
            .HasConversion<int>(); // Store enum as int, or use EnumToStringConverter if needed

        builder.HasIndex(p => p.DisplayOrder);

        // Configure navigation: Suggested side items (Main -> Side)
        builder.HasMany(p => p.SuggestedSideItems)
            .WithOne(si => si.MainProduct)
            .HasForeignKey(si => si.MainProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure other relationships (optional, if needed)
        builder.HasMany(p => p.Images)
            .WithOne(p => p.Product)
            .HasForeignKey("ProductId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ProductCategories)
            .WithOne(p => p.Product)
            .HasForeignKey("ProductId");

        builder.HasMany(p => p.Variations)
            .WithOne(p => p.Product)
            .HasForeignKey("ProductId");

        builder.HasMany(p => p.MenuProducts)
            .WithOne(p => p.Product)
            .HasForeignKey("ProductId");

        builder.HasMany(p => p.Descriptions)
            .WithOne(pd => pd.Product)
            .HasForeignKey(pd => pd.ProductId)
            .OnDelete(DeleteBehavior.Cascade);


    }
}
