using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderItemFactory
{
    private async Task<string?> AddMenuItemAsync(
        Order order, CreateOrderItemDto itemDto, bool pricesAreTrusted, bool metadataAreTrusted,
        CancellationToken cancellationToken)
    {
        var menu = await LoadMenuAsync(itemDto.MenuId, cancellationToken);
        if (menu is null)
            return $"Menu {itemDto.MenuId} not found";

        var menuProduct = menu.MenuItems.FirstOrDefault()?.Product;
        if (IsMenuRecipeUnavailable(menu, menuProduct))
            return "This menu's ingredient details are unavailable. Refresh the menu and try again.";

        var ingredientQuantities = ResolveMenuIngredientQuantities(itemDto, menuProduct);
        order.Items.Add(CreateMenuOrderItem(
            menu, menuProduct, itemDto, ingredientQuantities, pricesAreTrusted, metadataAreTrusted));

        return null;
    }

    private async Task<Menu?> LoadMenuAsync(Guid? menuId, CancellationToken cancellationToken)
    {
        // The recipe behind the menu's first item is what the order line's ingredient snapshot is
        // projected against — the same resolution the read path uses for a menu-backed line
        // (OrderIngredientCustomizations). Split, because MenuItems and the products' ingredient
        // collections cartesian-multiply in EF's default single-query mode.
        var menu = _tenantFeatures.EnforceSauceMinimum
            ? null
            : _context.Menus.Local.FirstOrDefault(
                candidate => candidate.Id == menuId && !candidate.IsDeleted);
        return menu ?? await _context.Menus
            .Include(candidate => candidate.MenuItems)
                .ThenInclude(item => item.Product.DetailedIngredients)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == menuId && !candidate.IsDeleted,
                cancellationToken);
    }

    private bool IsMenuRecipeUnavailable(Menu menu, Product? menuProduct) =>
        _tenantFeatures.EnforceSauceMinimum && menu.MenuItems.Count > 0 && menuProduct is null;

    private Dictionary<Guid, int>? ResolveMenuIngredientQuantities(
        CreateOrderItemDto itemDto, Product? menuProduct)
    {
        if (_tenantFeatures.EnforceSauceMinimum && menuProduct is not null)
        {
            return OrderLineIngredientChoice.Resolve(
                _lineCustomizationBuilder,
                itemDto,
                menuProduct,
                isRootLine: false).Quantities;
        }

        return itemDto.IngredientQuantities;
    }

    private OrderItem CreateMenuOrderItem(
        Menu menu,
        Product? menuProduct,
        CreateOrderItemDto itemDto,
        Dictionary<Guid, int>? ingredientQuantities,
        bool pricesAreTrusted,
        bool metadataAreTrusted)
    {
        // Menu lines keep Menus.BasePrice; product customization pricing cannot replace it.
        var unitPrice = menu.BasePrice;
        var customization = ResolveCustomizationPrice(itemDto, pricesAreTrusted);
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = menu.Name,
            VariationName = null,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice * itemDto.Quantity) + customization,
            SpecialInstructions = itemDto.SpecialInstructions,
            IngredientQuantitiesJson = SerializeIngredients(ingredientQuantities),
            IngredientSnapshots = BuildIngredientSnapshots(
                menuProduct?.DetailedIngredients,
                ingredientQuantities,
                itemDto,
                metadataAreTrusted),
            QuantityBasis = metadataAreTrusted ? itemDto.QuantityBasis ?? QuantityBasis.Unknown : QuantityBasis.Unknown,
            ConfigurationScope = metadataAreTrusted
                ? itemDto.ConfigurationScope ?? ConfigurationScope.Unknown
                : ConfigurationScope.Unknown,
            CompositionRole = metadataAreTrusted ? itemDto.CompositionRole ?? CompositionRole.Unknown : CompositionRole.Unknown,
            PresentationLabel = metadataAreTrusted ? itemDto.PresentationLabel : null,
            PresentationOrder = metadataAreTrusted ? itemDto.PresentationOrder : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        };
    }
}
