using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Features.Catalogue;

public sealed class CentralCatalogueSettings
{
    public const string SectionName = "Catalogue";

    /// <summary>The origin of the Sofra catalogue API, without a path.</summary>
    public string? ApiBaseUrl { get; set; }

    [Range(1, 30)]
    public int RequestTimeoutSeconds { get; set; } = 5;
}
