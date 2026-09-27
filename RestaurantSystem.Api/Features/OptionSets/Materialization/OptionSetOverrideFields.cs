namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetOverrideFields
{
    public static HashSet<string> From(OptionSetEntryOverride? value)
    {
        var fields = new HashSet<string>();
        if (value is null) return fields;
        if (value.Name is not null) fields.Add("name");
        if (value.DisplayOrder.HasValue) fields.Add("displayOrder");
        if (value.IsOptional.HasValue) fields.Add("isOptional");
        if (value.MaxQuantity.HasValue) fields.Add("maxQuantity");
        if (value.Price.HasValue) fields.Add("price");
        if (value.IsIncludedInBasePrice.HasValue) fields.Add("isIncludedInBasePrice");
        if (value.IsRequired.HasValue) fields.Add("isRequired");
        if (value.AdditionalPrice.HasValue) fields.Add("additionalPrice");
        if (value.IsDefault.HasValue) fields.Add("isDefault");
        return fields;
    }
}
