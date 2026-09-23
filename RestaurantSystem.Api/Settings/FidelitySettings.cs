using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class FidelitySettings
{
    public const string SectionName = "Fidelity";

    [Range(1, 1_000_000)]
    public int MaximumPointsPerRedemption { get; set; } = 100_000;
}
