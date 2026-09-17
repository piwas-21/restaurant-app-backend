namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>Hard bounds for the in-memory family grouping query.</summary>
internal static class CatalogQueryLimits
{
    /// <summary>Maximum number of product rows materialized for one catalogue request.</summary>
    public const int MaxProducts = 5000;
}
