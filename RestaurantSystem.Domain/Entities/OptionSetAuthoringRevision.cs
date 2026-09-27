using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

public class OptionSetAuthoringRevision : Entity
{
    public Guid? OptionSetId { get; set; }
    public Guid TargetProductId { get; set; }
    public Guid? TargetMenuSectionId { get; set; }
    public Guid? TargetCustomizationGroupId { get; set; }
    public int? SourceSetVersion { get; set; }
    public int? AppliedSetVersion { get; set; }
    public string SummaryJson { get; set; } = "{}";
}
