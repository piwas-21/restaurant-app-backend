using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Records an admin's accepted or rejected name-only match to prevent repeated guesses.</summary>
public class CatalogueMatchDecision : Entity
{
    public string SourceTemplateId { get; set; } = string.Empty;
    public int SourceRevision { get; set; }
    public string NormalizedName { get; set; } = string.Empty;
    public string CandidateType { get; set; } = string.Empty;
    public Guid CandidateId { get; set; }
    public CatalogueMatchDecisionStatus Decision { get; set; }
}
