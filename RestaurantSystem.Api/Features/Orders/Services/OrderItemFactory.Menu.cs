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
        // The recipe behind the menu's first item is what the order line's ingredient snapshot is
        // projected against — the same resolution the read path uses for a menu-backed line
        // (OrderIngredientCustomizations). Split, because MenuItems and the products' ingredient
        // collections cartesian-multiply in EF's default single-query mode.
        var menu = _tenantFeatures.EnforceSauceMinimum
            ? null
            : _context.Menus.Local.FirstOrDefault(
                candidate => candidate.Id == itemDto.MenuId && !candidate.IsDeleted);
        menu ??= await _context.Menus
            .Include(candidate => candidate.MenuItems)
                .ThenInclude(item => item.Product.DetailedIngredients)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == itemDto.MenuId && !candidate.IsDeleted,
                cancellationToken);

        if (menu == null)
        {
            return $"Menu {itemDto.MenuId} not found";
        }

        // The menu's first product is also the recipe used for its ingredient snapshot below. When
        // sauce-minimum enforcement is enabled, resolve the explicit selection against that recipe
        // just as the ProductId path does. Menu lines keep Menus.BasePrice; product customization
        // pricing cannot replace the menu's price. Skipping selection resolution while the flag is
        // off preserves the legacy MenuId payload exactly.
        var menuProduct = menu.MenuItems.FirstOrDefault()?.Product;
        if (_tenantFeatures.EnforceSauceMinimum && menu.MenuItems.Count > 0 && menuProduct is null)
        {
            return "This menu's ingredient details are unavailable. Refresh the menu and try again.";
        }

        var ingredientQuantities = itemDto.IngredientQuantities;
        if (_tenantFeatures.EnforceSauceMinimum && menuProduct is not null)
        {
            ingredientQuantities = OrderLineIngredientChoice.Resolve(
                _lineCustomizationBuilder,
                itemDto,
                menuProduct,
                isRootLine: false).Quantities;
        }

        var unitPrice = menu.BasePrice;
        var customization = ResolveCustomizationPrice(itemDto, pricesAreTrusted);
        order.Items.Add(new OrderItem
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
            IngredientSnapshots = OrderIngredientSnapshot.Build(
                menuProduct?.DetailedIngredients,
                ingredientQuantities,
                _currentUserService.GetAuditIdentifier(),
                metadataAreTrusted ? itemDto.IngredientQuantityBasis : QuantityBasis.Unknown,
                metadataAreTrusted ? itemDto.IngredientConfigurationScope : ConfigurationScope.Unknown,
                metadataAreTrusted ? itemDto.IngredientCompositionRoles : null),
            QuantityBasis = metadataAreTrusted ? itemDto.QuantityBasis ?? QuantityBasis.Unknown : QuantityBasis.Unknown,
            ConfigurationScope = metadataAreTrusted
                ? itemDto.ConfigurationScope ?? ConfigurationScope.Unknown
                : ConfigurationScope.Unknown,
            CompositionRole = metadataAreTrusted ? itemDto.CompositionRole ?? CompositionRole.Unknown : CompositionRole.Unknown,
            PresentationLabel = metadataAreTrusted ? itemDto.PresentationLabel : null,
            PresentationOrder = metadataAreTrusted ? itemDto.PresentationOrder : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        });

        return null;
    }
}
