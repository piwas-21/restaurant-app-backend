using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>An immutable upstream revision plus the tenant's selection and review choices.</summary>
public class CatalogueImportSessionTemplate : Entity
{
    public Guid SessionId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string Type { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string RevisionJson { get; set; } = string.Empty;
    public string? DecisionJson { get; set; }
    public bool IsRoot { get; set; }
    public bool IsSelectable { get; set; }
    public bool IsSelected { get; set; }
    public string? SelectionRole { get; set; }
    public CatalogueImportItemStatus Status { get; set; } = CatalogueImportItemStatus.Pending;
    public string? LocalEntityType { get; set; }
    public Guid? LocalEntityId { get; set; }
    public string? FailureCode { get; set; }

    public virtual CatalogueImportSession Session { get; set; } = null!;
}
