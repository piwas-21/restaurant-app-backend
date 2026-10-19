using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

public sealed class CustomerCheckoutItems(IBasketToOrderTranslator translator, IStaffCounterOrderPricing pricing,
    IOrderItemFactory items, IOrderChannelGuard channels, ApplicationDbContext context) : ICustomerCheckoutItems
{
    public async Task PopulateAsync(Order order, BasketDto basket, OrderType type, CancellationToken cancellationToken)
    {
        var translated = translator.Translate(basket.Items);
        CheckedLines(translated);
        var priced = await pricing.PriceBasketAsync(basket, translated, cancellationToken);
        var lines = CheckedLines(priced);
        await channels.EnsureOrderableAsync(priced, type, cancellationToken);
        // The shared batch pricer loads these complete graphs into DbSet.Local for the item factory.
        var products = context.Products.Local.ToDictionary(product => product.Id);
        var menus = context.Menus.Local.ToDictionary(menu => menu.Id);
        foreach (var line in lines)
        {
            EnsureProduct(line.ProductId, products);
            EnsureMenu(line.MenuId, menus);
        }
        foreach (var line in priced)
        {
            var error = await items.AddItemAsync(order, line, itemsAreServerPriced: true,
                cancellationToken, allowStaffPrices: false);
            if (error is not null) throw new BadRequestException(error);
        }
    }
    private static void EnsureProduct(Guid? id, Dictionary<Guid, Product> products)
    {
        if (id.HasValue && (!products.TryGetValue(id.Value, out var product)
            || !product.IsActive || !product.IsAvailable || product.IsDeleted))
            throw new BadRequestException("A selected product is unavailable. Review your basket.");
    }

    private static void EnsureMenu(Guid? id, Dictionary<Guid, Menu> menus)
    {
        if (id.HasValue && (!menus.TryGetValue(id.Value, out var menu)
            || !menu.IsActive || menu.IsDeleted
            || menu.MenuItems.Any(item => item.Product is null || !item.Product.IsActive || !item.Product.IsAvailable || item.Product.IsDeleted)))
            throw new BadRequestException("A selected menu is unavailable. Review your basket.");
    }

    private static Dtos.CreateOrderItemDto[] CheckedLines(IEnumerable<Dtos.CreateOrderItemDto> roots)
    {
        var lines = OrderInputTree.Flatten(roots).Take(CustomerCheckoutLimits.MaximumLines + 1).ToArray();
        if (lines.Length > CustomerCheckoutLimits.MaximumLines)
            throw new BadRequestException("This basket is too large for native checkout.");
        return lines;
    }

}
