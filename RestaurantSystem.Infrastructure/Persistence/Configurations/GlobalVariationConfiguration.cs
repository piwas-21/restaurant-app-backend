using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// The variation library (plan S4). Unlike <see cref="GlobalIngredientConfiguration"/>, which
/// deliberately configures almost nothing because its table was already live, this one ships WITH
/// the table it describes — so every column is bounded from the first migration rather than
/// arriving as unbounded <c>text</c> that a later slice has to tighten under live data.
/// </summary>
public class GlobalVariationConfiguration : IEntityTypeConfiguration<GlobalVariation>
{
    public void Configure(EntityTypeBuilder<GlobalVariation> builder)
    {
        builder.HasKey(g => g.Id);

        // 100, not the validator's 50: the library row is the template, and a tenant may keep a
        // longer label ("Menu with fries and a drink") than a single product's own variation name.
        builder.Property(g => g.DefaultName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(g => g.IsActive)
            .IsRequired();

        // Stored as its `int` value like every other enum on this model. Not indexed: the picker
        // reads the whole (≈50-row) catalog in one call and partitions it in the browser, so an
        // index here would buy nothing and cost every write.
        builder.Property(g => g.Origin)
            .HasConversion<int>()
            .HasDefaultValue(LibraryOrigin.System)
            .IsRequired();

        builder.Property(g => g.ArchivedBy)
            .HasMaxLength(200);

        builder.HasMany(g => g.Translations)
            .WithOne(t => t.GlobalVariation)
            .HasForeignKey(t => t.GlobalVariationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The picker orders by name and filters archived rows out; both are answered from the page
        // the query already loads, so the only index worth its writes is the one the archive drawer
        // and the shelf share.
        builder.HasIndex(g => g.DefaultName);
    }
}
