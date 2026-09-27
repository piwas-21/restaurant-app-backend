using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>One charged provider request; used for tenant rate and spend limits.</summary>
public sealed class TranslationGenerationBatch : Entity
{
    public string Fingerprint { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
}
