using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>
/// The single <see cref="Product"/> → <see cref="ProductSummaryDto"/> mapper, sibling to
/// <see cref="ProductDtoMapper"/> (which owns the full <c>ProductDto</c>).
/// </summary>
/// <remarks>
/// Extracted from <c>GetProductsQuery</c>, which was at 199 LOC against the 200-LOC
/// Command/Query/Handler limit (backend CLAUDE.md §4) and unbaselined — the availability field could
/// not be added inline. Keeping the projection here also means the next read path that needs a
/// summary reuses it instead of hand-rolling a fifth copy.
/// <para>
/// The caller must have loaded <c>Images</c>, <c>Descriptions</c>,
/// <c>ProductCategories → Category</c> and <c>Variations → Descriptions</c>.
/// </para>
/// </remarks>
public static class ProductSummaryMapper
{
    /// <param name="requestedOrderType">
    /// The channel the guest is ordering through, or <c>null</c> when they have not chosen one yet
    /// (the dominant browse state) — nothing is reported as blocked in that case.
    /// </param>
    public static ProductSummaryDto MapToSummaryDto(Product product, string baseUrl, OrderType? requestedOrderType)
    {
        var dto = new ProductSummaryDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            IsSpecial = product.IsSpecial,
            Type = product.Type,
            Allergens = product.Allergens,
            Ingredients = product.Ingredients,
            DetailedIngredients = [],
            Images = product.Images.Select(image => new ProductImageDto
            {
                Id = image.Id,
                Url = UrlJoin.Join(baseUrl, image.Url),
                IsPrimary = image.IsPrimary,
                SortOrder = image.SortOrder,
                AltText = image.AltText
            }).ToList(),
            // Soft-deleted categories are filtered out before `.Category` is read — see
            // `LiveProductCategories` for why the navigation can outlive its principal (§9.14).
            CategoryNames = LiveProductCategories.Of(product).Select(pc => pc.Category.Name).ToList(),
            PrimaryCategoryName = LiveProductCategories.Of(product)
                .Where(pc => pc.IsPrimary)
                .Select(pc => pc.Category.Name)
                .FirstOrDefault(),
            VariationCount = product.Variations.Count,
            Variations = product.Variations
                .Where(v => v.IsActive)
                .OrderBy(v => v.DisplayOrder)
                .Select(MapVariation)
                .ToList(),
            SuggestedSideItems = [],
            Content = new(),
            Availability = OrderTypeAvailability.Resolve(product, requestedOrderType),
            AvailableOrderTypes = product.AvailableOrderTypes
        };

        foreach (var description in product.Descriptions)
        {
            dto.Content[description.Lang] = new ProductDescriptionDto
            {
                Name = description.Name,
                Description = description.Description
            };
        }

        return dto;
    }

    private static ProductVariationDto MapVariation(ProductVariation variation) => new()
    {
        Id = variation.Id,
        Name = variation.Name,
        Description = variation.Description,
        PriceModifier = variation.PriceModifier,
        IsActive = variation.IsActive,
        DisplayOrder = variation.DisplayOrder,
        Content = variation.Descriptions
            .GroupBy(d => d.LanguageCode)
            .Select(g => g.First())
            .ToDictionary(
                d => d.LanguageCode,
                d => new ProductVariationContentDto
                {
                    Name = d.Name,
                    Description = d.Description
                })
    };
}
