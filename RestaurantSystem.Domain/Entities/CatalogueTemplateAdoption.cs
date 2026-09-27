using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Maps a central source revision to one tenant-local record within an adoption.</summary>
public class CatalogueTemplateAdoption : Entity
{
    public Guid AdoptionId { get; set; }
    public Guid SessionId { get; set; }
    public string SourceTemplateId { get; set; } = string.Empty;
    public int SourceRevision { get; set; }
    public string? SourceEntryId { get; set; }
    public string LocalEntityType { get; set; } = string.Empty;
    public Guid LocalEntityId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? BaselineFieldsJson { get; set; }
    public bool IsDefault { get; set; }
}
