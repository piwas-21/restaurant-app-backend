using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderItemFactory : IOrderItemFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public OrderItemFactory(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<string?> AddItemAsync(Order order, CreateOrderItemDto itemDto, CancellationToken cancellationToken)
    {
        if (itemDto.MenuId.HasValue)
        {
            return await AddMenuItemAsync(order, itemDto, cancellationToken);
        }

        if (itemDto.ProductId.HasValue)
        {
            await AddProductItemRecursiveAsync(order, itemDto, parentItem: null, cancellationToken);
        }

        // Neither MenuId nor ProductId — silently skip, matching the
        // original outer loop's fall-through behaviour.
        return null;
    }

    private async Task<string?> AddMenuItemAsync(Order order, CreateOrderItemDto itemDto, CancellationToken cancellationToken)
    {
        var menu = await _context.Menus
            .Include(p => p.MenuItems)
            .FirstOrDefaultAsync(p => p.Id == itemDto.MenuId && !p.IsDeleted, cancellationToken);

        if (menu == null)
        {
            return $"Menu {itemDto.MenuId} not found";
        }

        var unitPrice = menu.BasePrice;
        order.Items.Add(new OrderItem
        {
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = menu.Name,
            VariationName = null,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice * itemDto.Quantity) + itemDto.CustomizationPrice,
            SpecialInstructions = itemDto.SpecialInstructions,
            IngredientQuantitiesJson = SerializeIngredients(itemDto.IngredientQuantities),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        });

        return null;
    }

    private async Task AddProductItemRecursiveAsync(
        Order order,
        CreateOrderItemDto itemDto,
        OrderItem? parentItem,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.Variations)
            .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            // Throws — matches the original recursive method's behaviour for
            // both top-level and nested products. The top-level
            // not-found-as-Failure semantics only applies to menus.
            throw new NotFoundException($"Product {itemDto.ProductId} not found");
        }

        var (unitPrice, variationName) = ResolvePricing(itemDto, product);

        var orderItem = new OrderItem
        {
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = product.Name,
            VariationName = variationName,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice * itemDto.Quantity) + itemDto.CustomizationPrice,
            SpecialInstructions = itemDto.SpecialInstructions,
            IngredientQuantitiesJson = SerializeIngredients(itemDto.IngredientQuantities),
            ParentOrderItem = parentItem,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        };

        order.Items.Add(orderItem);

        if (itemDto.ChildItems != null)
        {
            foreach (var childDto in itemDto.ChildItems)
            {
                await AddProductItemRecursiveAsync(order, childDto, orderItem, cancellationToken);
            }
        }
    }

    // Two paths: either the client passed an explicit UnitPrice (variation
    // name still resolved for display), or we compute from BasePrice plus
    // the variation's PriceModifier.
    private static (decimal unitPrice, string? variationName) ResolvePricing(
        CreateOrderItemDto itemDto, Product product)
    {
        if (itemDto.UnitPrice > 0)
        {
            string? variationName = null;
            if (itemDto.ProductVariationId.HasValue)
            {
                var variation = product.Variations.FirstOrDefault(
                    v => v.Id == itemDto.ProductVariationId.Value && !v.IsDeleted);
                variationName = variation?.Name;
            }
            return (itemDto.UnitPrice, variationName);
        }

        var basePrice = product.BasePrice;
        if (itemDto.ProductVariationId.HasValue)
        {
            var variation = product.Variations.FirstOrDefault(
                v => v.Id == itemDto.ProductVariationId.Value && !v.IsDeleted);
            if (variation != null)
            {
                return (basePrice + variation.PriceModifier, variation.Name);
            }
        }
        return (basePrice, null);
    }

    private static string? SerializeIngredients(Dictionary<Guid, int>? ingredientQuantities) =>
        ingredientQuantities != null ? JsonSerializer.Serialize(ingredientQuantities) : null;
}
