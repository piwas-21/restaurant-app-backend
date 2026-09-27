using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>A tenant-local staging area; it never participates in guest catalogue queries.</summary>
public class CatalogueImportSession : Entity
{
    public string RootTemplateId { get; set; } = string.Empty;
    public int RootRevision { get; set; }
    public string Locale { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid AdoptionId { get; set; }
    public bool CreateNewCopy { get; set; }
    public string CreateSelectionJson { get; set; } = "[]";
    public CatalogueImportStatus Status { get; set; } = CatalogueImportStatus.Draft;
    public int Version { get; set; } = 1;
    public DateTime? CompletedAt { get; set; }
    public string? LastImportIdempotencyKey { get; set; }
    public int? LastImportExpectedVersion { get; set; }
    public string? LastImportResultJson { get; set; }

    public virtual ICollection<CatalogueImportSessionTemplate> Templates { get; set; } = [];
}
