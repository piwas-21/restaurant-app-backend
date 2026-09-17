using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class CatalogSettings
{
    public const string SectionName = "Catalog";

    [Range(1, 100_000)]
    public int MaxProducts { get; set; } = 5_000;

    [Range(1, 1_000)]
    public int MaxPageSize { get; set; } = 200;
}
