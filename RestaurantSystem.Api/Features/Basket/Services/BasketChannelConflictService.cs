using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

// Shared authoritative scan for web switches and read-only native conflict previews.
public sealed class BasketChannelConflictService(ApplicationDbContext context) : IBasketChannelConflictService
{
    /// <summary>
    /// Top-level lines the requested order type forbids — judged on the WHOLE line, including its
    /// bundle children and side items.
    /// </summary>
    /// <remarks>
    /// §9.15: this scan used to walk root rows only, which left §9.3's fix an add-then-switch twin.
    /// The ordinary guest journey defeated it: browse with NO channel chosen (permissive by design,
    /// and the dominant browse state) → add a combo whose component is takeaway-only → switch to
    /// Dine-in → zero conflicts, no confirm dialog, channel set → <c>OrderChannelGuard</c> flattens
    /// children at checkout and returns 400. The guest chose, was told nothing, and was refused at
    /// the till.
    /// <para>
    /// It now agrees with <c>OrderChannelGuard</c> by scanning the same set — see
    /// <see cref="BasketLineChannelScan"/>, which also owns the <c>ProductCategories → Category</c>
    /// include that keeps the verdict meaningful.
    /// </para>
    /// <para>
    /// Conflicts stay ROOT-granular even when the blocked part is a component: the reported
    /// <c>AllowedOrderTypes</c> is the intersection across the line, which is what
    /// <see cref="BasketChannelConflictDto.AllowedOrderTypes"/> already promises ("order types this
    /// line IS available on"). Naming the component instead would need a DTO field and a client
    /// change; naming the line is both true and actionable, since the line is what gets removed.
    /// </para>
    /// </remarks>
    public async Task<List<BasketChannelConflictDto>> FindAsync(
        Domain.Entities.Basket basket,
        OrderType orderType,
        CancellationToken cancellationToken)
    {
        var allItems = basket.Items.ToList();
        var roots = allItems.Where(i => i.ParentBasketItemId is null).ToList();

        var productIds = BasketLineChannelScan.CollectProductIds(roots, allItems);
        if (productIds.Count == 0)
        {
            return [];
        }

        var products = await BasketLineChannelScan.LoadProductsAsync(context, productIds, cancellationToken);

        var conflicts = new List<BasketChannelConflictDto>();
        foreach (var item in roots)
        {
            // Judge the line BEFORE resolving its name. An unresolvable root — its product
            // soft-deleted out from under the basket, which `DeleteProductCommand` does without
            // touching basket rows — must not discard its children unscanned: `OrderChannelGuard`
            // would still resolve those children at checkout and refuse the order, which is the
            // §9.15 symptom this method exists to prevent.
            var mask = BasketLineChannelScan.LineMask(item, allItems, products);
            if (OrderChannelMap.Allows(mask, orderType))
            {
                continue;
            }

            var product = item.ProductId is { } productId && products.TryGetValue(productId, out var resolved)
                ? resolved
                : null;

            conflicts.Add(new BasketChannelConflictDto
            {
                BasketItemId = item.Id,
                ProductId = product?.Id ?? item.ProductId,
                // Empty rather than a placeholder, because that is already what every other surface
                // shows for this line: `BasketMappingService` reads the product null-conditionally,
                // so a deleted-product line renders nameless in the cart too. Inventing a name here
                // would need a locale key for a state the rest of the app leaves blank.
                ProductName = product?.Name ?? string.Empty,
                Quantity = item.Quantity,
                AllowedOrderTypes = OrderChannelMap.ToOrderTypes(mask)
            });
        }

        return conflicts;
    }
}
