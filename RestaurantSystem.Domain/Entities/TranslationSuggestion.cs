using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>A model suggestion kept separate from guest-visible text until ordinary Save.</summary>
public sealed class TranslationSuggestion : Entity
{
    public Guid BatchId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? ClientKey { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string ContextHash { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string SuggestedText { get; set; } = string.Empty;
    public string? ReviewedText { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Status { get; set; } = "suggested";
    public string RequestedBy { get; set; } = string.Empty;
    public string? ReviewerId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
