using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class OptionSetAttachment : Entity
{
    public Guid OptionSetId { get; set; }
    public OptionSetAttachmentRole Role { get; set; }
    public Guid TargetProductId { get; set; }
    public Guid? TargetMenuSectionId { get; set; }
    public Guid? TargetCustomizationGroupId { get; set; }
    public int AppliedSetVersion { get; set; }
    public int Version { get; set; } = 1;
    public int? MinSelection { get; set; }
    public int? MaxSelection { get; set; }
    public int? IncludedFree { get; set; }
    public int DisplayOrder { get; set; }
    public string? IntentionalDifferenceReason { get; set; }
    public string? LastIdempotencyKey { get; set; }
    public virtual OptionSet OptionSet { get; set; } = null!;
    public virtual ICollection<OptionSetAppliedRow> AppliedRows { get; set; } = [];
}
