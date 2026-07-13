using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// The single <c>Product</c> (Type=Menu) → <see cref="MenuBundleDto"/> mapper for the bundle list
/// and detail queries (menu-bundles redesign #156). Produces the full nested tree
/// (sections → items → per-option <c>DetailedIngredients</c>) the customization drill-in needs; the
/// dead per-option <c>SuggestedSideItems</c> (removed at both ends in slice 1) are no longer
/// projected. Previously the list query carried the full tree while the detail query returned a thin
/// subset — this unifies them (list keeps its ingredients; detail gains them). The caller loads the
/// navigations it reads (menu-definition → sections → items → product → detailed-ingredients →
/// descriptions, plus descriptions and images).
/// </summary>
public static class MenuBundleMapper
{
    public static MenuBundleDto MapToMenuBundleDto(Product product, string baseUrl)
    {
        var dto = new MenuBundleDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            IsSpecial = product.IsSpecial,
            PreparationTimeMinutes = product.PreparationTimeMinutes,
            Type = "menu",
            DisplayOrder = product.DisplayOrder,
            MenuDefinition = product.MenuDefinition != null ? new MenuDefinitionDto
            {
                Id = product.MenuDefinition.Id,
                IsAlwaysAvailable = product.MenuDefinition.IsAlwaysAvailable,
                StartTime = product.MenuDefinition.StartTime?.ToString(@"hh\:mm\:ss"),
                EndTime = product.MenuDefinition.EndTime?.ToString(@"hh\:mm\:ss"),
                AvailableMonday = product.MenuDefinition.AvailableMonday,
                AvailableTuesday = product.MenuDefinition.AvailableTuesday,
                AvailableWednesday = product.MenuDefinition.AvailableWednesday,
                AvailableThursday = product.MenuDefinition.AvailableThursday,
                AvailableFriday = product.MenuDefinition.AvailableFriday,
                AvailableSaturday = product.MenuDefinition.AvailableSaturday,
                AvailableSunday = product.MenuDefinition.AvailableSunday,
                Sections = product.MenuDefinition.Sections.OrderBy(s => s.DisplayOrder).Select(s => new MenuSectionDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    DisplayOrder = s.DisplayOrder,
                    IsRequired = s.IsRequired,
                    MinSelection = s.MinSelection,
                    MaxSelection = s.MaxSelection,
                    Items = s.Items.OrderBy(i => i.DisplayOrder).Select(i => new MenuSectionItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product?.Name,
                        AdditionalPrice = i.AdditionalPrice,
                        DisplayOrder = i.DisplayOrder,
                        IsDefault = i.IsDefault,
                        Ingredients = i.Product != null ? (i.Product.DetailedIngredients.Any()
                            ? i.Product.DetailedIngredients.Where(di => di.IsActive).Select(di => di.Name).ToList()
                            : i.Product.Ingredients) : null,
                        Allergens = i.Product?.Allergens,
                        DetailedIngredients = i.Product?.DetailedIngredients
                            .Where(di => di.IsActive)
                            .OrderBy(di => di.DisplayOrder)
                            .Select(di => new ProductIngredientDto
                            {
                                Id = di.Id,
                                Name = di.Name,
                                IsOptional = di.IsOptional,
                                Price = di.Price,
                                IsIncludedInBasePrice = di.IsIncludedInBasePrice,
                                IsActive = di.IsActive,
                                DisplayOrder = di.DisplayOrder,
                                MaxQuantity = di.MaxQuantity,
                                Content = di.Descriptions?.ToDictionary(
                                    desc => desc.LanguageCode,
                                    desc => new ProductIngredientContentDto
                                    {
                                        Name = desc.Name,
                                        Description = desc.Description
                                    }
                                )
                            }).ToList()
                    }).ToList()
                }).ToList()
            } : null,
            Content = new(),
            Images = product.Images.Select(i => new RestaurantSystem.Api.Features.Products.Dtos.ProductImageDto
            {
                Id = i.Id,
                Url = UrlJoin.Join(baseUrl, i.Url),
                AltText = i.AltText,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder
            }).OrderBy(i => i.SortOrder).ToList()
        };

        foreach (var description in product.Descriptions)
        {
            dto.Content[description.Lang] = new MenuBundleContentDto
            {
                Name = description.Name,
                Description = description.Description
            };
        }
        return dto;
    }
}
