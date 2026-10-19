using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Resolves every counter line against the live catalogue before OrderItemFactory runs.</summary>
public sealed partial class StaffCounterOrderPricing : IStaffCounterOrderPricing
{
    private readonly ApplicationDbContext _context;
    private readonly ILineCustomizationBuilder _customizations;

    public StaffCounterOrderPricing(
        ApplicationDbContext context, ILineCustomizationBuilder customizations)
    {
        _context = context;
        _customizations = customizations;
    }

    public async Task<List<CreateOrderItemDto>> PriceAsync(
        IReadOnlyCollection<CreateOrderItemDto> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var products = await LoadProductsAsync(items, cancellationToken);
        return items.Select(item => PriceLine(item, products)).ToList();
    }

    private CreateOrderItemDto PriceLine(
        CreateOrderItemDto source, IReadOnlyDictionary<Guid, Product> products, bool sourceIsBasket = false)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.ProductId.HasValue)
        {
            if (source.ChildItems is { Count: > 0 })
            {
                throw new BadRequestException("A menu order line cannot contain child items.");
            }

            return source with { UnitPrice = 0m, CustomizationPrice = 0m };
        }
        var product = products.GetValueOrDefault(source.ProductId.Value)
            ?? throw new NotFoundException($"Product {source.ProductId.Value} not found");
        BasketComponentGuard.EnsureNotOrderedAlone(product);
        var variation = ResolveVariation(product, source.ProductVariationId);
        return PriceLine(source, product, variation, products, sourceIsBasket);
    }

    private CreateOrderItemDto PriceLine(
        CreateOrderItemDto source, Product product, ProductVariation? variation,
        IReadOnlyDictionary<Guid, Product> products, bool sourceIsBasket = false)
    {
        if (source.Quantity <= 0)
        {
            throw new BadRequestException("Item quantity must be greater than zero.");
        }

        // This is the same effective base-row rule as BasketService. A stale POS screen cannot
        // de-select a required variation, and an inactive variation cannot keep its old modifier.
        BasketBaseProductGuard.EnsureVariationChosen(product, variation);

        var explicitCustomization = StaffExplicitCustomization.Resolve(product, source);
        var customization = _customizations.Build(
            product.DetailedIngredients, explicitCustomization.SelectedIngredientIds,
            explicitCustomization.IngredientQuantities,
            preferProvidedQuantities: false,
            options: LineCustomizationOptions.FromProduct(product));
        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0m)
            + explicitCustomization.ProductOptionPrice;
        var children = source.ChildItems;
        if (product.Type == ProductType.Menu)
        {
            return PriceBundle(
                source, product, variation, customization, children, products, sourceIsBasket);
        }

        if (children is not { Count: > 0 })
        {
            return source with
            {
                UnitPrice = unitPrice,
                // OrderItemFactory adds CustomizationPrice once, after UnitPrice * Quantity. The
                // builder returns a per-unit figure, so the staff DTO must carry a line-absolute one.
                CustomizationPrice = customization.CustomizationPrice * source.Quantity,
                IngredientQuantities = customization.IngredientQuantities,
                ChildItems = explicitCustomization.ProductOptionChildren
            };
        }

        var priced = PriceSideItems(source, unitPrice, customization, children, products, sourceIsBasket);
        priced.ChildItems!.AddRange(explicitCustomization.ProductOptionChildren);
        return priced;
    }

    private CreateOrderItemDto PriceSideItems(
        CreateOrderItemDto source, decimal unitPrice, LineCustomization customization,
        List<CreateOrderItemDto> children, IReadOnlyDictionary<Guid, Product> products, bool sourceIsBasket = false)
    {
        decimal sidesPerParent = 0m;
        var pricedChildren = new List<CreateOrderItemDto>(children.Count);
        foreach (var child in children)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!child.ProductId.HasValue)
            {
                throw new BadRequestException("A side item must reference a product.");
            }

            if (child.Kind is not null && child.Kind != OrderItemKind.SideItem)
            {
                throw new BadRequestException("Only side items may be nested under a regular counter line.");
            }

            if (child.Quantity <= 0)
            {
                throw new BadRequestException("Side-item quantity must be greater than zero.");
            }

            var pricedChild = PriceLine(child, products, sourceIsBasket);
            sidesPerParent += pricedChild.UnitPrice * child.Quantity;
            // The child quantity is per parent unit and its customization is already absolute for
            // that child line. Scale it by the root quantity so OrderItemFactory adds the full amount.
            pricedChildren.Add(pricedChild with
            {
                Kind = OrderItemKind.SideItem,
                CustomizationPrice = pricedChild.CustomizationPrice * source.Quantity
            });
        }

        return source with
        {
            UnitPrice = unitPrice + sidesPerParent,
            CustomizationPrice = customization.CustomizationPrice * source.Quantity,
            IngredientQuantities = customization.IngredientQuantities,
            ChildItems = pricedChildren
        };
    }

    private async Task<IReadOnlyDictionary<Guid, Product>> LoadProductsAsync(
        IReadOnlyCollection<CreateOrderItemDto> items, CancellationToken cancellationToken, bool includeSides = false)
    {
        var lines = OrderInputTree.Flatten(items).ToList();
        var menuIds = lines
            .Where(item => item.MenuId.HasValue)
            .Select(item => item.MenuId!.Value)
            .Distinct()
            .ToList();
        if (menuIds.Count > 0)
        {
            // Keep this tracked: OrderItemFactory reuses the complete graph from DbSet.Local while
            // constructing the same staff order, avoiding a second per-line catalogue walk.
            await _context.Menus
                .Include(menu => menu.MenuItems)
                    .ThenInclude(item => item.Product.DetailedIngredients)
                .AsSplitQuery()
                .Where(menu => menuIds.Contains(menu.Id) && !menu.IsDeleted)
                .LoadAsync(cancellationToken);
        }

        var ids = lines
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();
        var query = _context.Products
            .Include(item => item.Variations)
            .Include(item => item.DetailedIngredients)
            .Include(item => item.CustomizationGroups)
                .ThenInclude(group => group.IngredientOptions)
                    .ThenInclude(option => option.ProductIngredient)
            .Include(item => item.CustomizationGroups)
                .ThenInclude(group => group.ProductOptions)
                    .ThenInclude(option => option.OptionProduct)
            .Include(item => item.MenuDefinition!.Sections)
                    .ThenInclude(section => section.Items)
                        .ThenInclude(item => item.ProductVariation)
            .AsSplitQuery()
            .Where(item => ids.Contains(item.Id) && !item.IsDeleted);
        if (includeSides) query = query.Include(item => item.SuggestedSideItems);
        var products = await query.ToListAsync(cancellationToken);
        return products.ToDictionary(item => item.Id);
    }

    private static ProductVariation? ResolveVariation(Product product, Guid? variationId)
    {
        if (!variationId.HasValue)
        {
            return null;
        }

        var variation = product.Variations.FirstOrDefault(
            item => item.Id == variationId.Value && !item.IsDeleted && item.IsActive);
        return variation ?? throw new BadRequestException("Selected product variation is not available.");
    }
}
