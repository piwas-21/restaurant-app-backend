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
/// <para>
/// Since #468 <see cref="MapDefinition"/> is also what the PRODUCT reads project a bundle's
/// sections through (<c>GetProductByIdQuery</c>, <c>ProductDtoMapper</c>), so one bundle read the
/// same way by two endpoints answers the same thing. It previously did not.
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
            MenuDefinition = product.MenuDefinition != null
                ? MapDefinition(product.MenuDefinition)
                : null,
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
    /// A bundle's schedule and its sections, in the READ contract's shape — the one projection
    /// behind <c>GET /api/Menus</c>, <c>GET /api/Menus/{id}</c> and <c>GET /api/Products/{id}</c>.
    /// </summary>
    /// <remarks>
    /// Extracted for backend #468. The product read had a projection of its own that carried an
    /// option row's identity and price and nothing else — no recipe, no sauce rule, no allergens —
    /// so a guest who opened a bundle by PRODUCT id got a combo with nothing to customize, and the
    /// child line it posted carried no <c>selectedIngredientIds</c>. Two projections of one row is
    /// the defect; the fix is that there is now one.
    /// <para>
    /// The caller must have loaded sections → items → product → detailed-ingredients →
    /// descriptions. An unloaded collection is EMPTY, not absent, so a forgotten include here does
    /// not throw: it serves an option product whose recipe reads as "this dish has no ingredients".
    /// </para>
    /// </remarks>
    public static MenuBundleDefinitionDto MapDefinition(MenuDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new MenuBundleDefinitionDto
        {
            Id = definition.Id,
            IsAlwaysAvailable = definition.IsAlwaysAvailable,
            StartTime = definition.StartTime?.ToString(@"hh\:mm\:ss"),
            EndTime = definition.EndTime?.ToString(@"hh\:mm\:ss"),
            AvailableMonday = definition.AvailableMonday,
            AvailableTuesday = definition.AvailableTuesday,
            AvailableWednesday = definition.AvailableWednesday,
            AvailableThursday = definition.AvailableThursday,
            AvailableFriday = definition.AvailableFriday,
            AvailableSaturday = definition.AvailableSaturday,
            AvailableSunday = definition.AvailableSunday,
            Sections = definition.Sections
                .OrderBy(s => s.DisplayOrder)
                .Select(MapSection)
                .ToList()
        };
    }

    private static MenuBundleSectionDto MapSection(MenuSection section) => new()
    {
        Id = section.Id,
        Name = section.Name,
        Description = section.Description,
        DisplayOrder = section.DisplayOrder,
        IsRequired = section.IsRequired,
        MinSelection = section.MinSelection,
        MaxSelection = section.MaxSelection,
        // A section that lists a DELETED product went on offering it to guests, and the basket then
        // refuses the line. The filter lives HERE and not in the callers' includes because one of
        // those callers (`GetProductByIdQuery`) runs `IgnoreQueryFilters()`, which un-filters every
        // include — so the soft-delete rule cannot be left to the global filter on that read.
        Items = section.Items
            .Where(i => i.Product != null && !i.Product.IsDeleted)
            .OrderBy(i => i.DisplayOrder)
            .Select(MapSectionItem)
            .ToList()
    };

    private static MenuBundleSectionItemDto MapSectionItem(MenuSectionItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductName = item.Product?.Name,
        AdditionalPrice = item.AdditionalPrice,
        DisplayOrder = item.DisplayOrder,
        IsDefault = item.IsDefault,
        Ingredients = SectionItemIngredients(item.Product),
        Allergens = item.Product?.Allergens,
        // The option product's OWN sauce rule (S6) — the same row BasketItemFactory prices the
        // child line with. A missing product means no rule to state, so the defaults (0 / null / 0)
        // say "no sauce group", which is what a product that never mentions sauces carries anyway.
        SauceMin = item.Product?.SauceMin ?? 0,
        SauceMax = item.Product?.SauceMax,
        SauceIncludedFree = item.Product?.SauceIncludedFree ?? 0,
        DetailedIngredients = item.Product?.DetailedIngredients
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
    };

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

        // Ordered by DisplayOrder like the DetailedIngredients list beside it. It was unordered,
        // which means the DATABASE decided it — and since #468 two different queries feed this one
        // mapper, "whatever order the rows came back in" is a contract two endpoints can disagree
        // about on the same row. Display order is what a display list wants anyway.
        return product.DetailedIngredients.Count > 0
            ? product.DetailedIngredients
                .Where(di => di.IsActive)
                .OrderBy(di => di.DisplayOrder)
                .Select(di => di.Name)
                .ToList()
            : product.Ingredients;
    }
}
