using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

/// <summary>
/// Enforces "at most one primary category per product" in the DATABASE (§9.5).
/// </summary>
/// <remarks>
/// <para>
/// There was no configuration for this entity at all, so nothing stopped two rows for one product
/// carrying <c>IsPrimary = true</c>. That is not a tidiness concern: <c>OrderTypeAvailability.EffectiveMask</c>
/// resolves an inheriting product through <c>ProductCategories.FirstOrDefault(pc =&gt; pc.IsPrimary)</c>,
/// so with two primaries a product's channel restriction depends on **row load order** — the same
/// item can be orderable on one request and refused on the next, with nothing in the data to
/// explain it.
/// </para>
/// <para>
/// A FILTERED unique index rather than a plain one: only the primary rows are constrained, so a
/// product keeps as many secondary categories as it likes. The predicate is written against the
/// snake_case column names the Npgsql naming convention produces.
/// </para>
/// </remarks>
public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        // Keep the plain product_id index EF's convention created: the filtered one below only
        // covers primary rows, so dropping it would leave every "categories of this product" lookup
        // without an index. Declared explicitly because adding a second HasIndex on the same leading
        // column otherwise makes EF replace it.
        builder.HasIndex(pc => pc.ProductId)
            .HasDatabaseName("ix_product_categories_product_id");

        builder.HasIndex(pc => new { pc.ProductId, pc.IsPrimary })
            .IsUnique()
            .HasFilter("\"is_primary\" = true")
            .HasDatabaseName("ix_product_categories_product_id_is_primary_unique");
    }
}
