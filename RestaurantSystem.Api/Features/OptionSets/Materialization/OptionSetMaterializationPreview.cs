namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetMaterializationPreview
{
    public Guid OptionSetId { get; set; }
    public int SetVersion { get; set; }
    public List<OptionSetMaterializationTargetPreviewDto> Targets { get; set; } = [];
    public List<OptionSetRelatedOfferWarningDto> RelatedOfferWarnings { get; set; } = [];
}
