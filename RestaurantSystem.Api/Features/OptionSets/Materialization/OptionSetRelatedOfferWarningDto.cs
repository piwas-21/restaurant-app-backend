namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetRelatedOfferWarningDto
{
    public string TargetKey { get; set; } = string.Empty;
    public Guid RelatedProductId { get; set; }
    public string RelatedProductName { get; set; } = string.Empty;
    public string RelatedOfferType { get; set; } = string.Empty;
    public Guid? RelatedVariationId { get; set; }
    public string RelatedTargetRole { get; set; } = string.Empty;
    public Guid? RelatedTargetId { get; set; }
    public string? RelatedTargetName { get; set; }
    public bool ReasonRequired { get; set; } = true;
}
