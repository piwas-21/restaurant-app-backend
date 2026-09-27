using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationTargetRequest
{
    public string TargetKey { get; set; } = string.Empty;
    public OptionSetAttachmentRole Role { get; set; }
    public Guid TargetProductId { get; set; }
    public Guid? TargetMenuSectionId { get; set; }
    public Guid? TargetCustomizationGroupId { get; set; }
    public int? ExpectedMenuAuthoringVersion { get; set; }
    public int? ExpectedCustomizationGroupVersion { get; set; }
    public int? ExpectedAttachmentVersion { get; set; }
    public IReadOnlyList<Guid>? EntryIds { get; set; }
    public IReadOnlyDictionary<Guid, OptionSetEntryOverride>? Overrides { get; set; }
    public OptionSetConflictPolicy ConflictPolicy { get; set; } = OptionSetConflictPolicy.PreserveLocal;
    public OptionSetAttachmentSettings? Settings { get; set; }
    public string? IntentionalDifferenceReason { get; set; }
}
