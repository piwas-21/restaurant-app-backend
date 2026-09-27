using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class OptionSetMatchDecision : Entity
{
    public string NormalizedName { get; set; } = string.Empty;
    public string CandidateType { get; set; } = string.Empty;
    public Guid CandidateId { get; set; }
    public bool IsAccepted { get; set; }
    public string? Alias { get; set; }
}
