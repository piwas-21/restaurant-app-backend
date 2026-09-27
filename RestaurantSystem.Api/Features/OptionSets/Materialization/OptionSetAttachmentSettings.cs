namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetAttachmentSettings
{
    public int? MinSelection { get; set; }
    public int? MaxSelection { get; set; }
    public int? IncludedFree { get; set; }
    public int? DisplayOrder { get; set; }
}
