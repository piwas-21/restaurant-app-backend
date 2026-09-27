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

/// <inheritdoc />
public partial class OrderItemFactory : IOrderItemFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILineCustomizationBuilder _lineCustomizationBuilder;
    private readonly ITenantFeatures _tenantFeatures;

    public OrderItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILineCustomizationBuilder lineCustomizationBuilder,
        ITenantFeatures tenantFeatures)
    {
        _context = context;
        _currentUserService = currentUserService;
        _lineCustomizationBuilder = lineCustomizationBuilder;
        _tenantFeatures = tenantFeatures;
    }

    public async Task<string?> AddItemAsync(
        Order order, CreateOrderItemDto itemDto, bool itemsAreServerPriced,
        CancellationToken cancellationToken, bool allowStaffPrices = true)
    {
        // Prices in the DTO are honoured from two sources and no others: the persisted basket
        // (itemsAreServerPriced), and a staff member standing behind the till. Everyone else gets
        // catalogue pricing. The staff carve-out is the same one OrderPaymentBuilder already draws
        // for tenders — the POS legitimately hand-builds composed lines, and taking that away in
        // the name of guarding an anonymous endpoint would break the till instead.
        // Legacy callers retain the staff carve-out for composed lines. The authenticated counter
        // contract opts out explicitly: its payload is still a hand-built request, so only catalogue
        // values (or values prepared by a server-owned quote) may affect money.
        var pricesAreTrusted = itemsAreServerPriced
            || (allowStaffPrices && _currentUserService.IsStaff);

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
        // Neither MenuId nor ProductId: preserve the original fall-through.
        return null;
    }

    private Task<bool> IsBundleAsync(Guid productId, CancellationToken cancellationToken) =>
        _context.Products.AnyAsync(
            p => p.Id == productId && !p.IsDeleted && p.Type == ProductType.Menu, cancellationToken);

    private async Task<string?> AddMenuItemAsync(
        Order order, CreateOrderItemDto itemDto, bool pricesAreTrusted, CancellationToken cancellationToken)
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
                _currentUserService.GetAuditIdentifier()),
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
        // DetailedIngredients is loaded for the ingredient snapshot below, not for pricing — money
        // is settled before this factory runs (see ResolvePricing). Sibling collections, hence split.
        var product = _context.Products.Local.FirstOrDefault(
            candidate => candidate.Id == itemDto.ProductId && !candidate.IsDeleted);
        product ??= await _context.Products
            .Include(candidate => candidate.Variations)
            .Include(candidate => candidate.DetailedIngredients)
            .Include(candidate => candidate.CustomizationGroups)
                .ThenInclude(group => group.IngredientOptions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == itemDto.ProductId && !candidate.IsDeleted,
                cancellationToken);

        if (product == null)
        {
            // Throws — matches the original recursive method's behaviour for
            // both top-level and nested products. The top-level
            // not-found-as-Failure semantics only applies to menus.
            throw new NotFoundException($"Product {itemDto.ProductId} not found");
        }

        if (parentItem is null)
        {
            BasketComponentGuard.EnsureNotOrderedAlone(product);
        }

        // What the line SAID about its ingredients, resolved against the recipe (#430). When it
        // carried a structured selection the server prices the line itself, so the declared
        // UnitPrice and CustomizationPrice are both dropped — `Price is not null` is that verdict.
        // See OrderLineIngredientChoice for which lines it covers and why the rest are excluded.
        var choice = OrderLineIngredientChoice.Resolve(
            _lineCustomizationBuilder, itemDto, product, isRootLine: parentItem == null);

        var (unitPrice, variationName) = ResolvePricing(itemDto, product, pricesAreTrusted && choice.Price is null);
        var customization = choice.Price ?? ResolveCustomizationPrice(itemDto, pricesAreTrusted);
        if (choice.Price is not null && parentItem != null && itemDto.Kind == OrderItemKind.SideItem)
        {
            customization *= parentItem.Quantity;
        }

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
        var itemTotal = ResolveItemTotal(
            parentItem, unitPrice, itemDto.Quantity, customization);

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
            IngredientQuantitiesJson = SerializeIngredients(choice.Quantities),
            // THE FREEZE POINT for the ingredient half of the line. Everything else on this row is
            // already a snapshot (ProductName, VariationName, UnitPrice, ItemTotal); until S1 the
            // ingredients were bare ids re-resolved against the live catalog on every read, so a
            // rename or a delete rewrote a receipt already printed. D2, owner-confirmed 2026-08-24.
            IngredientSnapshots = OrderIngredientSnapshot.Build(
                product.DetailedIngredients,
                choice.Quantities,
                _currentUserService.GetAuditIdentifier()),
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


}
