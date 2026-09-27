using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Dtos;

public sealed class OptionSetAttachmentDto
{
    public Guid Id { get; set; }
    public OptionSetAttachmentRole Role { get; set; }
    public Guid TargetProductId { get; set; }
    public Guid? TargetMenuSectionId { get; set; }
    public Guid? TargetCustomizationGroupId { get; set; }
    public int AppliedSetVersion { get; set; }
    public int Version { get; set; }
    public int? MinSelection { get; set; }
    public int? MaxSelection { get; set; }
    public int? IncludedFree { get; set; }
    public int DisplayOrder { get; set; }
    public string? IntentionalDifferenceReason { get; set; }
}
