namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueCurrentRevisionBatchRequestValidator
{
    public static bool IsValid(IReadOnlyList<CatalogueCurrentRevisionRequest>? items) =>
        items is { Count: >= 1 and <= CatalogueCurrentRevisionBatchLimits.MaximumTenantBatchItems } &&
        items.All(item => item is not null && !string.IsNullOrWhiteSpace(item.TemplateId) &&
            item.TemplateId.Length <= CatalogueCurrentRevisionBatchLimits.MaximumTemplateIdLength &&
            item.AdoptedRevision > 0) &&
        items.Select(item => item.TemplateId).Distinct(StringComparer.Ordinal).Count() == items.Count;
}
