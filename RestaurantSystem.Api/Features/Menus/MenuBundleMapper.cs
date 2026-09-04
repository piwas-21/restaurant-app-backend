using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
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
/// <para>
/// Since §9.2 the caller must ALSO load <c>ProductCategories → Category</c>: a bundle with no mask
/// of its own inherits its primary category's, and an unloaded collection resolves as UNRESTRICTED
/// — a missing include here is a silently permissive verdict, not an error.
/// </para>
/// </summary>
public static class MenuBundleMapper
{
    /// <param name="requestedOrderType">
    /// The channel the guest is ordering through, or <c>null</c> when they have not chosen one (the
    /// dominant browse state) — nothing is reported as blocked in that case.
    /// </param>
    public static MenuBundleDto MapToMenuBundleDto(Product product, string baseUrl, OrderType? requestedOrderType)
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
            Availability = OrderTypeAvailability.Resolve(product, requestedOrderType),
            AvailableOrderTypes = product.AvailableOrderTypes,
            // The bundle's OWN labelling. The one other `Allergens` in this file is a section
            // item's, and mapping only that meant a labelled combo reached the guest indis-
            // tinguishable from an unlabelled one — which the menu filter reads as "free of
            // everything" (#477).
            Allergens = product.Allergens,
            MenuDefinition = product.MenuDefinition != null ? new MenuBundleDefinitionDto
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
                Sections = product.MenuDefinition.Sections.OrderBy(s => s.DisplayOrder).Select(s => new MenuBundleSectionDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    DisplayOrder = s.DisplayOrder,
                    IsRequired = s.IsRequired,
                    MinSelection = s.MinSelection,
                    MaxSelection = s.MaxSelection,
                    Items = s.Items.OrderBy(i => i.DisplayOrder).Select(i => new MenuBundleSectionItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product?.Name,
                        AdditionalPrice = i.AdditionalPrice,
                        DisplayOrder = i.DisplayOrder,
                        IsDefault = i.IsDefault,
                        Ingredients = SectionItemIngredients(i.Product),
                        Allergens = i.Product?.Allergens,
                        // The option product's OWN sauce rule (S6) — the same row BasketItemFactory
                        // prices the child line with. A missing product means no rule to state, so
                        // the defaults (0 / null / 0) say "no sauce group", which is what a product
                        // that never mentions sauces carries anyway.
                        SauceMin = i.Product?.SauceMin ?? 0,
                        SauceMax = i.Product?.SauceMax,
                        SauceIncludedFree = i.Product?.SauceIncludedFree ?? 0,
                        DetailedIngredients = i.Product?.DetailedIngredients
                            .Where(di => di.IsActive)
                            .OrderBy(di => di.DisplayOrder)
                            .Select(di => new MenuBundleIngredientDto
                            {
                                Id = di.Id,
                                Name = di.Name,
                                IsOptional = di.IsOptional,
                                Price = di.Price,
                                IsIncludedInBasePrice = di.IsIncludedInBasePrice,
                                IsActive = di.IsActive,
                                DisplayOrder = di.DisplayOrder,
                                MaxQuantity = di.MaxQuantity,
                                Kind = di.Kind,
                                ExclusionGroup = di.ExclusionGroup,
                                Content = di.Descriptions?
                                    .GroupBy(desc => desc.LanguageCode)
                                    .Select(g => g.First()) // first wins on duplicate language codes
                                    .ToDictionary(
                                        desc => desc.LanguageCode,
                                        desc => new MenuBundleIngredientContentDto
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
            Images = product.Images.Select(i => new ProductImageDto
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

    /// <summary>
    /// A section-item's display ingredient names: the active detailed-ingredient names when the
    /// child product has any, otherwise its plain string ingredient list (null when no product).
    /// </summary>
    private static List<string>? SectionItemIngredients(Product? product)
    {
        if (product == null)
        {
            return null;
        }

        return product.DetailedIngredients.Any()
            ? product.DetailedIngredients.Where(di => di.IsActive).Select(di => di.Name).ToList()
            : product.Ingredients;
    }
}
