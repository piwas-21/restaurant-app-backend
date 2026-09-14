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
public sealed class StaffCounterOrderPricing : IStaffCounterOrderPricing
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
        CreateOrderItemDto source, IReadOnlyDictionary<Guid, Product> products)
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
        var variation = ResolveVariation(product, source.ProductVariationId);
        return PriceLine(source, product, variation, products);
    }

    private CreateOrderItemDto PriceLine(
        CreateOrderItemDto source, Product product, ProductVariation? variation,
        IReadOnlyDictionary<Guid, Product> products)
    {
        if (source.Quantity <= 0)
        {
            throw new BadRequestException("Item quantity must be greater than zero.");
        }

        // This is the same effective base-row rule as BasketService. A stale POS screen cannot
        // de-select a required variation, and an inactive variation cannot keep its old modifier.
        BasketBaseProductGuard.EnsureVariationChosen(product, variation);

        var customization = _customizations.Build(
            product.DetailedIngredients, source.SelectedIngredientIds, source.IngredientQuantities,
            preferProvidedQuantities: false, product.SauceIncludedFree, product.SauceMax);
        var unitPrice = product.BasePrice + (variation?.PriceModifier ?? 0m);
        var children = source.ChildItems;

        if (product.Type == ProductType.Menu)
        {
            return PriceBundle(
                source, product, variation, customization, children, products);
        }

        if (children is not { Count: > 0 })
        {
            return source with
            {
                UnitPrice = unitPrice,
                // OrderItemFactory adds CustomizationPrice once, after UnitPrice * Quantity. The
                // builder returns a per-unit figure, so the staff DTO must carry a line-absolute one.
                CustomizationPrice = customization.CustomizationPrice * source.Quantity,
                IngredientQuantities = customization.IngredientQuantities
            };
        }

        return PriceSideItems(
            source, unitPrice, customization, children, products);
    }

    private CreateOrderItemDto PriceBundle(
        CreateOrderItemDto source, Product product, ProductVariation? variation,
        LineCustomization customization, List<CreateOrderItemDto>? children,
        IReadOnlyDictionary<Guid, Product> products)
    {
        var sections = product.MenuDefinition?.Sections.ToList()
            ?? throw new NotFoundException("Menu definition not found");
        var sourceChildren = children ?? [];
        var selections = new List<SelectedMenuOptionDto>(sourceChildren.Count);
        var sectionItems = new List<(CreateOrderItemDto Child, MenuSectionItem Item)>(sourceChildren.Count);

        foreach (var child in sourceChildren)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!child.ProductId.HasValue)
            {
                throw new BadRequestException("A bundle option must reference a product.");
            }

            if (!child.SectionId.HasValue || child.SectionId.Value == Guid.Empty)
            {
                throw new BadRequestException("A bundle option must identify its menu section.");
            }

            if (child.Quantity <= 0 || child.Quantity % source.Quantity != 0)
            {
                throw new BadRequestException("Bundle option quantity does not match the parent quantity.");
            }

            var optionQuantity = child.Quantity / source.Quantity;
            var sectionItem = MenuBundleSelectionRules.ResolveSectionItem(
                sections, child.SectionId.Value, child.ProductId.Value);
            sectionItems.Add((child, sectionItem));
            selections.Add(new SelectedMenuOptionDto
            {
                SectionId = child.SectionId.Value,
                ItemId = child.ProductId.Value,
                Quantity = optionQuantity,
                SpecialInstructions = child.SpecialInstructions,
                SelectedIngredients = child.SelectedIngredientIds,
                IngredientQuantities = child.IngredientQuantities
            });
        }

        // The same required/min/max/membership rule as the Basket menu path. Quantities passed to
        // this helper are per parent unit; the staff wire keeps child quantities line-absolute.
        var optionsPerParent = MenuBundleSelectionRules.ValidateAndSumOptionPrices(sections, selections);
        var pricedChildren = new List<CreateOrderItemDto>(sectionItems.Count);
        foreach (var (child, sectionItem) in sectionItems)
        {
            var childProduct = products.GetValueOrDefault(child.ProductId!.Value)
                ?? throw new NotFoundException($"Product {child.ProductId.Value} not found");
            var childVariation = ResolveVariation(childProduct, child.ProductVariationId);
            BasketBaseProductGuard.EnsureVariationChosen(childProduct, childVariation);
            var pricedChild = PriceLine(child, childProduct, childVariation, products);

            // A bundle section replaces the option's base price, but never its variation modifier
            // (or any composed surcharge already resolved on that option).
            var childUnitPrice = sectionItem.AdditionalPrice
                + (pricedChild.UnitPrice - childProduct.BasePrice);
            pricedChildren.Add(pricedChild with
            {
                UnitPrice = childUnitPrice,
                Kind = OrderItemKind.BundleChild
            });
        }

        return source with
        {
            UnitPrice = product.BasePrice + (variation?.PriceModifier ?? 0m) + optionsPerParent,
            // The parent can carry its own map-only/selection customization. It is per unit in the
            // shared builder and line-absolute in CreateOrderItemDto.
            CustomizationPrice = customization.CustomizationPrice * source.Quantity,
            IngredientQuantities = customization.IngredientQuantities,
            ChildItems = pricedChildren
        };
    }

    private CreateOrderItemDto PriceSideItems(
        CreateOrderItemDto source, decimal unitPrice, LineCustomization customization,
        List<CreateOrderItemDto> children, IReadOnlyDictionary<Guid, Product> products)
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

            var pricedChild = PriceLine(child, products);
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
        IReadOnlyCollection<CreateOrderItemDto> items, CancellationToken cancellationToken)
    {
        var lines = Flatten(items).ToList();
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
        var products = await _context.Products
            .Include(item => item.Variations)
            .Include(item => item.DetailedIngredients)
            .Include(item => item.MenuDefinition!.Sections)
                    .ThenInclude(section => section.Items)
            .AsSplitQuery()
            .Where(item => ids.Contains(item.Id) && !item.IsDeleted)
            .ToListAsync(cancellationToken);
        return products.ToDictionary(item => item.Id);
    }

    private static IEnumerable<CreateOrderItemDto> Flatten(
        IEnumerable<CreateOrderItemDto> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.ChildItems is not null)
            {
                foreach (var child in Flatten(item.ChildItems))
                {
                    yield return child;
                }
            }
        }
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
