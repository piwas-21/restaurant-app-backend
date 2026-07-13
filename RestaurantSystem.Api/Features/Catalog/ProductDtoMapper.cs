using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>
/// The single <see cref="Product"/> → <see cref="ProductDto"/> mapper, shared by the product and
/// menu-bundle create/update commands (menu-bundles redesign #156). Replaces three near-copies
/// (one full-product mapper + two identical bundle-only subsets). For a bundle the product-specific
/// collections (<c>DetailedIngredients</c>/<c>Variations</c>/<c>SuggestedSideItems</c>) are empty
/// rather than omitted — a uniform, harmless response shape. The caller must have loaded the
/// navigations it reads (categories→category, variations→descriptions, side-items→side-item
/// product, detailed-ingredients→descriptions, menu-definition→sections→items→product, descriptions);
/// bundle products carry the product-specific collections empty, so those nested reads are no-ops.
/// </summary>
public static class ProductDtoMapper
{
    public static ProductDto MapToProductDto(Product product)
    {
        var dto = new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            PreparationTimeMinutes = product.PreparationTimeMinutes,
            Type = product.Type,
            KitchenType = product.KitchenType,
            Ingredients = product.Ingredients,
            Allergens = product.Allergens,
            DisplayOrder = product.DisplayOrder,
            DetailedIngredients = product.DetailedIngredients.Select(di => new ProductIngredientDto
            {
                Id = di.Id,
                Name = di.Name,
                IsOptional = di.IsOptional,
                Price = di.Price,
                IsIncludedInBasePrice = di.IsIncludedInBasePrice,
                IsActive = di.IsActive,
                DisplayOrder = di.DisplayOrder,
                MaxQuantity = di.MaxQuantity,
                Content = ToLocalizedContent(di.Descriptions, d => d.LanguageCode,
                    d => new ProductIngredientContentDto { Name = d.Name, Description = d.Description })
            }).ToList(),
            Categories = product.ProductCategories.Select(pc => new ProductCategoryDto
            {
                CategoryId = pc.CategoryId,
                CategoryName = pc.Category.Name,
                IsPrimary = pc.IsPrimary,
                DisplayOrder = pc.DisplayOrder
            }).ToList(),
            PrimaryCategory = product.ProductCategories
                .Where(pc => pc.IsPrimary)
                .Select(pc => new CategoryDto
                {
                    Id = pc.Category.Id,
                    Name = pc.Category.Name,
                    Description = pc.Category.Description,
                    ImageUrl = pc.Category.ImageUrl,
                    IsActive = pc.Category.IsActive,
                    DisplayOrder = pc.Category.DisplayOrder
                })
                .FirstOrDefault(),
            Variations = product.Variations.Select(v => new ProductVariationDto
            {
                Id = v.Id,
                Name = v.Name,
                Description = v.Description,
                PriceModifier = v.PriceModifier,
                FinalPrice = product.BasePrice + v.PriceModifier,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
                Content = ToLocalizedContent(v.Descriptions, d => d.LanguageCode,
                    d => new ProductVariationContentDto { Name = d.Name, Description = d.Description })
            }).ToList(),
            SuggestedSideItems = product.SuggestedSideItems.Select(si => new SideItemDto
            {
                Id = si.SideItemProduct.Id,
                Name = si.SideItemProduct.Name,
                Description = si.SideItemProduct.Description,
                Price = si.SideItemProduct.BasePrice,
                IsRequired = si.IsRequired,
                DisplayOrder = si.DisplayOrder
            }).ToList(),
            MenuDefinition = product.MenuDefinition != null ? new MenuDefinitionDto
            {
                Id = product.MenuDefinition.Id,
                IsAlwaysAvailable = product.MenuDefinition.IsAlwaysAvailable,
                StartTime = product.MenuDefinition.StartTime,
                EndTime = product.MenuDefinition.EndTime,
                AvailableMonday = product.MenuDefinition.AvailableMonday,
                AvailableTuesday = product.MenuDefinition.AvailableTuesday,
                AvailableWednesday = product.MenuDefinition.AvailableWednesday,
                AvailableThursday = product.MenuDefinition.AvailableThursday,
                AvailableFriday = product.MenuDefinition.AvailableFriday,
                AvailableSaturday = product.MenuDefinition.AvailableSaturday,
                AvailableSunday = product.MenuDefinition.AvailableSunday,
                Sections = product.MenuDefinition.Sections.Select(s => new MenuSectionDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    DisplayOrder = s.DisplayOrder,
                    IsRequired = s.IsRequired,
                    MinSelection = s.MinSelection,
                    MaxSelection = s.MaxSelection,
                    Items = s.Items.Select(i => new MenuSectionItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product?.Name,
                        AdditionalPrice = i.AdditionalPrice,
                        DisplayOrder = i.DisplayOrder,
                        IsDefault = i.IsDefault
                    }).OrderBy(i => i.DisplayOrder).ToList()
                }).OrderBy(s => s.DisplayOrder).ToList()
            } : null,
            Content = new()
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

    /// <summary>
    /// Projects a set of localized descriptions into a language-code → content map, taking the
    /// first entry when a language code is duplicated. Shared by the ingredient and variation
    /// content maps (which differ only by their content DTO type).
    /// </summary>
    private static Dictionary<string, TContent> ToLocalizedContent<TDescription, TContent>(
        IEnumerable<TDescription> descriptions,
        Func<TDescription, string> languageCode,
        Func<TDescription, TContent> content)
        => descriptions
            .GroupBy(languageCode)
            .Select(g => g.First()) // first wins on duplicate language codes
            .ToDictionary(languageCode, content);
}
