namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationTargetResultDto
{
    public string TargetKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? AttachmentId { get; set; }
    public int? AttachmentVersion { get; set; }
    public int? MenuAuthoringVersion { get; set; }
    public int? CustomizationGroupVersion { get; set; }
    public List<OptionSetMaterializationAppliedRowDto> AppliedRows { get; set; } = [];
    public List<OptionSetMaterializationConflictDto> Conflicts { get; set; } = [];
}
