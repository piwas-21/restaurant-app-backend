using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Tenant-owned evidence for one accepted or manually maintained localized field.</summary>
public sealed class TranslationFieldProvenance : Entity
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string SourceLocale { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string TextHash { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public string? TemplateId { get; set; }
    public int? TemplateRevision { get; set; }
    public string? ReviewerId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
