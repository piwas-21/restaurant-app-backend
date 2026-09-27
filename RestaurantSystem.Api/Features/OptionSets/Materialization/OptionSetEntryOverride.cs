namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

public sealed class OptionSetEntryOverride
{
    public string? Name { get; set; }
    public int? DisplayOrder { get; set; }
    public bool? IsOptional { get; set; }
    public int? MaxQuantity { get; set; }
    public decimal? Price { get; set; }
    public bool? IsIncludedInBasePrice { get; set; }
    public bool? IsRequired { get; set; }
    public decimal? AdditionalPrice { get; set; }
    public bool? IsDefault { get; set; }
}
