namespace RestaurantSystem.Domain.Entities;

public partial class Product
{
    /// <summary>Stable-ID customer customization screen manifest; null derives the legacy order.</summary>
    public string? CustomerStepManifestJson { get; set; }
    /// <summary>Optimistic concurrency revision for customer customization screen authoring.</summary>
    public int CustomerStepManifestRevision { get; set; }
}
