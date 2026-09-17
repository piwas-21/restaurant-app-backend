using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>Groups public products into explicit offer families without name-based inference.</summary>
internal static class CatalogOfferFamilyBuilder
{
    public static List<CatalogOfferFamilyDto> Build(
        IReadOnlyCollection<Product> products,
        Guid? categoryId,
        OrderType? requestedOrderType,
        DayOfWeek day,
        TimeSpan timeOfDay,
        string baseUrl)
    {
        var byId = products.ToDictionary(p => p.Id);
        var menuSchedule = MenuScheduleWindow.AvailableAt(day, timeOfDay).Compile();
        var schedule = (Product product) => product.Type != ProductType.Menu
            || product.MenuDefinition is null
            || menuSchedule(product);
        var childrenByParent = products
            .Where(product => product.MenuDefinition?.ParentOfferProductId is not null)
            .GroupBy(product => product.MenuDefinition!.ParentOfferProductId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
        var roots = products
            .Where(product => IsPublicAnchor(product) || IsRoot(product, byId))
            .Where(product => product.IsActive && !product.IsDeleted && !product.IsComponent)
            .Where(product => IsPlacedInCategory(product, categoryId))
            .Where(product => IsVisibleAtSchedule(product, byId, childrenByParent, schedule))
            .OrderBy(PrimaryCategoryOrder)
            .ThenBy(product => product.DisplayOrder)
            .ThenBy(product => product.Name)
            .ThenBy(product => product.Id)
            .ToList();

        return roots
            .Select(root => MapFamily(root, byId, childrenByParent, requestedOrderType, schedule, baseUrl))
            .ToList();
    }

    private static CatalogOfferFamilyDto MapFamily(
        Product root,
        Dictionary<Guid, Product> byId,
        Dictionary<Guid, List<Product>> childrenByParent,
        OrderType? requestedOrderType,
        Func<Product, bool> schedule,
        string baseUrl)
    {
        var menuOffers = childrenByParent.TryGetValue(root.Id, out var children)
            ? children
            .Where(product => product.IsActive
                && product.Type == ProductType.Menu
                && IsValidChild(product, root, byId))
            .OrderBy(product => product.MenuDefinition!.ParentOfferVariationId.HasValue)
            .ThenBy(product => product.DisplayOrder)
            .ThenBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Select(menu => new CatalogMenuOfferDto
            {
                ProductId = menu.Id,
                ParentVariationId = menu.MenuDefinition!.ParentOfferVariationId,
                Price = menu.BasePrice,
                Availability = OrderTypeAvailability.Resolve(menu, requestedOrderType),
                ScheduleAvailable = schedule(menu),
                Allergens = menu.Allergens
            })
            .ToList()
            : [];

        var categoryIds = LiveProductCategories.Of(root)
            .OrderBy(category => category.DisplayOrder)
            .Select(category => category.CategoryId)
            .ToList();

        // A linked menu whose inactive/component/deleted parent was not public is intentionally
        // mapped as an independent card. Do not return the stale relationship fields in that
        // card: they would leak the unavailable anchor's identity and invite the client to group
        // the menu back into a family it must not render.
        var anchor = ProductSummaryMapper.MapToSummaryDto(
            root, baseUrl, requestedOrderType, exposeOfferParentLink: false);
        var startingPrice = StartingPrice(root, menuOffers, requestedOrderType, schedule);

        return new CatalogOfferFamilyDto
        {
            Id = root.Id,
            Anchor = anchor,
            MenuOffers = menuOffers,
            AnchorScheduleAvailable = schedule(root),
            VisibleInAll = IsVisibleInAll(root),
            CategoryIds = categoryIds,
            StartingPrice = startingPrice
        };
    }

    private static bool IsRoot(Product product, Dictionary<Guid, Product> byId)
    {
        var parentId = product.MenuDefinition?.ParentOfferProductId;
        if (product.Type != ProductType.Menu || parentId is null)
        {
            return true;
        }

        if (!byId.TryGetValue(parentId.Value, out var parent) || !IsPublicAnchor(parent))
        {
            return true;
        }

        if (!IsValidChild(product, parent, byId))
        {
            return true;
        }

        return false;
    }

    private static bool IsPublicAnchor(Product product) =>
        product.IsActive && !product.IsDeleted && !product.IsComponent
        && product.MenuDefinition?.ParentOfferProductId is null;

    private static bool IsValidChild(
        Product child,
        Product parent,
        Dictionary<Guid, Product> byId)
    {
        if (child.Type != ProductType.Menu || child.MenuDefinition?.ParentOfferProductId != parent.Id)
        {
            return false;
        }

        var sourceVariationId = child.MenuDefinition.ParentOfferVariationId;
        return sourceVariationId is null
            || parent.Variations.Any(v => v.Id == sourceVariationId.Value && v.IsActive && !v.IsDeleted);
    }

    private static bool IsPlacedInCategory(Product product, Guid? categoryId)
    {
        var categories = LiveProductCategories.Of(product).ToList();
        if (categoryId.HasValue)
        {
            return categories.Any(category => category.CategoryId == categoryId.Value);
        }

        return true;
    }

    private static bool IsVisibleInAll(Product product) =>
        GuestAllViewVisibility.IsVisibleInAll(product);

    private static bool IsVisibleAtSchedule(
        Product root,
        Dictionary<Guid, Product> byId,
        Dictionary<Guid, List<Product>> childrenByParent,
        Func<Product, bool> schedule)
    {
        if (root.Type != ProductType.Menu || schedule(root))
        {
            return true;
        }

        return childrenByParent.TryGetValue(root.Id, out var children)
            && children.Any(product => product.IsActive
            && product.Type == ProductType.Menu
            && IsValidChild(product, root, byId)
            && schedule(product));
    }

    private static decimal StartingPrice(
        Product root,
        IReadOnlyCollection<CatalogMenuOfferDto> menuOffers,
        OrderType? requestedOrderType,
        Func<Product, bool> schedule)
    {
        var orderable = new List<decimal>();
        if (!BaseProductVisibility.IsBaseHidden(root)
            && root.IsActive
            && root.IsAvailable
            && schedule(root)
            && OrderTypeAvailability.Resolve(root, requestedOrderType).CanOrder)
        {
            orderable.Add(root.BasePrice);
        }

        orderable.AddRange(root.Variations
            .Where(variation => variation.IsActive && !variation.IsDeleted && root.IsActive && root.IsAvailable
                && schedule(root)
                && OrderTypeAvailability.Resolve(root, requestedOrderType).CanOrder)
            .Select(variation => root.BasePrice + variation.PriceModifier));

        orderable.AddRange(menuOffers
            .Where(menu => menu.ScheduleAvailable && menu.Availability.CanOrder)
            .Select(menu => menu.Price));

        if (orderable.Count > 0)
        {
            return orderable.Min();
        }

        var fallback = new List<decimal> { root.BasePrice };
        fallback.AddRange(root.Variations
            .Where(variation => variation.IsActive && !variation.IsDeleted)
            .Select(variation => root.BasePrice + variation.PriceModifier));
        fallback.AddRange(menuOffers.Select(menu => menu.Price));
        return fallback.Min();
    }

    private static int PrimaryCategoryOrder(Product product) =>
        LiveProductCategories.Of(product)
            .Where(category => category.IsPrimary)
            .Select(category => category.Category.DisplayOrder)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
}
