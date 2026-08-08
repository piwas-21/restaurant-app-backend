using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
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

    public async Task<string?> AddItemAsync(
        Order order, CreateOrderItemDto itemDto, bool itemsAreServerPriced, CancellationToken cancellationToken)
    {
        // Prices in the DTO are honoured from two sources and no others: the persisted basket
        // (itemsAreServerPriced), and a staff member standing behind the till. Everyone else gets
        // catalogue pricing. The staff carve-out is the same one OrderPaymentBuilder already draws
        // for tenders — the POS legitimately hand-builds composed lines, and taking that away in
        // the name of guarding an anonymous endpoint would break the till instead.
        var pricesAreTrusted = itemsAreServerPriced || _currentUserService.IsStaff;

        if (itemDto.MenuId.HasValue)
        {
            return await AddMenuItemAsync(order, itemDto, pricesAreTrusted, cancellationToken);
        }

        if (itemDto.ProductId.HasValue)
        {
            // A composed line is REFUSED on the untrusted path rather than priced from the
            // catalogue. Its real price is the parent's base plus the selected options', and those
            // option prices live in the menu definition — `product.BasePrice` alone cannot express
            // it. Falling back to the base price would silently UNDERCHARGE the bundle (measured:
            // 8.00 against a true 12.98), trading a large hole for a smaller one. Refusing keeps
            // the rule honest: this endpoint accepts only what it can price itself.
            //
            // The real checkout is unaffected — /from-basket is server-priced, and its bundles are
            // covered end to end by BasketToOrderIntegrationTest and OrderLineCustomizationPriceTests.
            // Two shapes are refused, not one. Child rows are the obvious composed line — but a
            // BUNDLE PARENT POSTED ALONE has no children and would slip through to be priced at
            // `product.BasePrice`, which is exactly the 8.00-against-a-true-12.98 undercharge this
            // guard exists to prevent. ProductType.Menu is what makes a product a bundle.
            if (!pricesAreTrusted &&
                (itemDto.ChildItems is { Count: > 0 } || await IsBundleAsync(itemDto.ProductId.Value, cancellationToken)))
            {
                return "A composed item cannot be ordered through this endpoint; check out from the basket instead.";
            }

            await AddProductItemRecursiveAsync(order, itemDto, parentItem: null, pricesAreTrusted, cancellationToken);
        }

        // Neither MenuId nor ProductId — silently skip, matching the
        // original outer loop's fall-through behaviour.
        return null;
    }

    private Task<bool> IsBundleAsync(Guid productId, CancellationToken cancellationToken) =>
        _context.Products.AnyAsync(
            p => p.Id == productId && !p.IsDeleted && p.Type == ProductType.Menu, cancellationToken);

    private async Task<string?> AddMenuItemAsync(
        Order order, CreateOrderItemDto itemDto, bool pricesAreTrusted, CancellationToken cancellationToken)
    {
        var menu = await _context.Menus
            .Include(p => p.MenuItems)
            .FirstOrDefaultAsync(p => p.Id == itemDto.MenuId && !p.IsDeleted, cancellationToken);

        if (menu == null)
        {
            return $"Menu {itemDto.MenuId} not found";
        }

        var unitPrice = menu.BasePrice;
        var customization = ResolveCustomizationPrice(itemDto, pricesAreTrusted);
        order.Items.Add(new OrderItem
        {
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = menu.Name,
            VariationName = null,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = (unitPrice * itemDto.Quantity) + customization,
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
        bool pricesAreTrusted,
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

        var (unitPrice, variationName) = ResolvePricing(itemDto, product, pricesAreTrusted);
        var customization = ResolveCustomizationPrice(itemDto, pricesAreTrusted);

        // Convention mirrors BasketService.AddItemToBasketAsync (Features/Basket/Services/BasketService.cs:230-245):
        // child rows carry UnitPrice for display but ItemTotal = 0, because the
        // parent's ItemTotal already includes the rolled-up combo price.
        // Without this, any caller that goes through OrderPricingService's
        // legacy compute path (no command.BasketSubTotal — e.g. admin tooling,
        // bulk import, refunds-as-new-orders) double-counts every child's
        // UnitPrice on top of the parent. See issue #54.
        //
        // A child's CustomizationPrice (e.g. extra toppings on a child pizza
        // option) is NOT pre-rolled into the parent's UnitPrice by all callers.
        // BasketService rolls it up by adding to the parent's ItemTotal/UnitPrice
        // (BasketService.cs:215, 243-245). OrderItem has no CustomizationPrice
        // column, so we add the child's CustomizationPrice contribution
        // directly to the parent's ItemTotal here. (DTO contract: per
        // CreateOrderItemDto.cs:11-14, CustomizationPrice is "for the WHOLE line,
        // not per unit", so no extra Quantity multiplier — consistent with the
        // top-level branch below and the menu path on line 61. BasketToOrderTranslator
        // sends 0 here for both child kinds, so no basket-sourced DTO reaches this
        // line at all; it exists for a caller that hand-builds POST /api/orders.)
        decimal itemTotal;
        if (parentItem != null)
        {
            itemTotal = 0m;
            if (customization != 0m)
            {
                // Walk up to the top-level root: every intermediate parent's
                // ItemTotal must stay 0 (BasketService convention — see
                // OrderItemFactoryTests.cs grandchild test). At grandchild
                // depth, rolling into the immediate parent would silently
                // drop the customization because the next level zeros it.
                // PR #67 review.
                var root = parentItem;
                while (root.ParentOrderItem != null)
                {
                    root = root.ParentOrderItem;
                }
                root.ItemTotal += customization;
            }
        }
        else
        {
            itemTotal = (unitPrice * itemDto.Quantity) + customization;
        }

        var orderItem = new OrderItem
        {
            ProductId = itemDto.ProductId,
            ProductVariationId = itemDto.ProductVariationId,
            MenuId = itemDto.MenuId,
            ProductName = product.Name,
            VariationName = variationName,
            Quantity = itemDto.Quantity,
            UnitPrice = unitPrice,
            ItemTotal = itemTotal,
            SpecialInstructions = itemDto.SpecialInstructions,
            IngredientQuantitiesJson = SerializeIngredients(itemDto.IngredientQuantities),
            ParentOrderItem = parentItem,
            // A kind belongs to a CHILD row. Discarded on a root even if a caller sent one, so the
            // column cannot come to mean two things (#318).
            Kind = parentItem != null ? itemDto.Kind : null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUserService.GetAuditIdentifier(),
        };

        order.Items.Add(orderItem);

        if (itemDto.ChildItems != null)
        {
            foreach (var childDto in itemDto.ChildItems)
            {
                await AddProductItemRecursiveAsync(order, childDto, orderItem, pricesAreTrusted, cancellationToken);
            }
        }
    }

    // An explicit UnitPrice is honoured ONLY when the price is trusted — the items came from the
    // persisted basket via IBasketToOrderTranslator (where a bundle's rolled-up unit price and a
    // variation's modifier are already resolved), or the caller is staff.
    //
    // For a hand-built POST /api/orders body the price is taken from the catalogue instead. That
    // endpoint is ANONYMOUS, so honouring its UnitPrice let a caller name its own price: posting
    // `unitPrice: 0.01` against a 12.99 product produced a 0.01 order. It is the same defect class
    // as the client-declared BasketTotal that S0b removed, one level further down — closing only
    // the total would have left the line items to say the same untrue thing.
    private static (decimal unitPrice, string? variationName) ResolvePricing(
        CreateOrderItemDto itemDto, Product product, bool pricesAreTrusted)
    {
        if (pricesAreTrusted && itemDto.UnitPrice > 0)
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

    // CustomizationPrice is added straight to a line's ItemTotal, so it is a price lever in its own
    // right and is dropped for the same reason as UnitPrice above. A basket-sourced DTO always
    // sends 0 here for child rows, so this costs the real checkout path nothing.
    private static decimal ResolveCustomizationPrice(CreateOrderItemDto itemDto, bool pricesAreTrusted) =>
        pricesAreTrusted ? itemDto.CustomizationPrice : 0m;

    private static string? SerializeIngredients(Dictionary<Guid, int>? ingredientQuantities) =>
        ingredientQuantities != null ? JsonSerializer.Serialize(ingredientQuantities) : null;
}
