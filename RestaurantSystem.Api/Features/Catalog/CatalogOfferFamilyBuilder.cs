using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Api.Features.Menus;
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
        var roots = products
            .Where(product => IsRoot(product, byId))
            .Where(product => IsPlacedInCategory(product, categoryId))
            .Where(product => IsVisibleAtSchedule(product, byId, schedule))
            .OrderBy(PrimaryCategoryOrder)
            .ThenBy(product => product.DisplayOrder)
            .ThenBy(product => product.Name)
            .ThenBy(product => product.Id)
            .ToList();

        return roots
            .Select(root => MapFamily(root, products, byId, requestedOrderType, schedule, baseUrl))
            .ToList();
    }

    private static CatalogOfferFamilyDto MapFamily(
        Product root,
        IReadOnlyCollection<Product> products,
        Dictionary<Guid, Product> byId,
        OrderType? requestedOrderType,
        Func<Product, bool> schedule,
        string baseUrl)
    {
        var menuOffers = root.IsAvailable
            ? products
            .Where(product => product.Type == ProductType.Menu
                && product.MenuDefinition?.ParentOfferProductId == root.Id
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

        var anchor = ProductSummaryMapper.MapToSummaryDto(root, baseUrl, requestedOrderType);
        var startingPrice = StartingPrice(root, menuOffers, requestedOrderType, schedule);

        return new CatalogOfferFamilyDto
        {
            Id = root.Id,
            Anchor = anchor,
            MenuOffers = menuOffers,
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

        if (!byId.TryGetValue(parentId.Value, out var parent) || !CanAnchor(parent))
        {
            return true;
        }

        return !IsValidChild(product, parent, byId);
    }

    private static bool CanAnchor(Product product) =>
        product.IsAvailable && product.MenuDefinition?.ParentOfferProductId is null;

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

        return categories.Count == 0
            || categories.Any(category => !category.Category.IsHiddenFromAllTab);
    }

    private static bool IsVisibleAtSchedule(
        Product root,
        Dictionary<Guid, Product> byId,
        Func<Product, bool> schedule)
    {
        if (root.Type != ProductType.Menu || schedule(root))
        {
            return true;
        }

        return byId.Values.Any(product => product.Type == ProductType.Menu
            && product.MenuDefinition?.ParentOfferProductId == root.Id
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
            && root.IsAvailable
            && schedule(root)
            && OrderTypeAvailability.Resolve(root, requestedOrderType).CanOrder)
        {
            orderable.Add(root.BasePrice);
        }

        orderable.AddRange(root.Variations
            .Where(variation => variation.IsActive && !variation.IsDeleted && root.IsAvailable
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
