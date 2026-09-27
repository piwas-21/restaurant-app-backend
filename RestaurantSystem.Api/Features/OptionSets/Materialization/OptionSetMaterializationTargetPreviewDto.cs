namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationTargetPreviewDto
{
    public string TargetKey { get; set; } = string.Empty;
    public Guid TargetProductId { get; set; }
    public Guid? TargetMenuSectionId { get; set; }
    public Guid? TargetCustomizationGroupId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? AttachmentId { get; set; }
    public int? CurrentAttachmentVersion { get; set; }
    public int? CurrentMenuAuthoringVersion { get; set; }
    public int? CurrentCustomizationGroupVersion { get; set; }
    public List<OptionSetMaterializationConflictDto> Conflicts { get; set; } = [];
    public List<OptionSetMaterializationChangeDto> Changes { get; set; } = [];
}
