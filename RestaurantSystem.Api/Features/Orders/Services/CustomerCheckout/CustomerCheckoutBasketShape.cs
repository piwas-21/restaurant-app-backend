using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;
using DomainBasket = RestaurantSystem.Domain.Entities.Basket;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutBasketShape
{
    internal static void Ensure(DomainBasket basket)
    {
        if (basket.Items.Count > CustomerCheckoutLimits.MaximumLines) throw Unsupported();
        var count = basket.Items.Count;
        foreach (var row in basket.Items)
        {
            EnsureQuantities(row);
            count += Sides(row).Count;
            if (count > CustomerCheckoutLimits.MaximumLines) throw Unsupported();
        }
        var children = basket.Items.Where(item => item.ParentBasketItemId.HasValue)
            .ToLookup(item => item.ParentBasketItemId!.Value);
        var remaining = new Stack<(BasketItem Item, int Depth)>(basket.Items
            .Where(item => item.ParentBasketItemId is null).Select(item => (item, 0)));
        var seen = new HashSet<Guid>();
        while (remaining.TryPop(out var row))
        {
            if (row.Depth > CustomerCheckoutLimits.MaximumDepth || row.Item.Id == Guid.Empty
                || !seen.Add(row.Item.Id) || row.Item.Quantity <= 0) throw Unsupported();
            foreach (var child in children[row.Item.Id]) remaining.Push((child, row.Depth + 1));
        }
        if (seen.Count != basket.Items.Count) throw Unsupported();
    }

    internal static void EnsureView(DomainBasket basket, BasketDto view)
    {
        var mapped = new Dictionary<Guid, BasketItemDto>();
        var pending = new Stack<BasketItemDto>(view.Items);
        while (pending.TryPop(out var row))
        {
            if (row.Id is null || !mapped.TryAdd(row.Id.Value, row)) throw Unsupported();
            foreach (var child in row.ChildItems ?? []) pending.Push(child);
        }
        if (mapped.Count != basket.Items.Count) throw Unsupported();
        foreach (var row in basket.Items)
        {
            if (!mapped.TryGetValue(row.Id, out var target)) throw Unsupported();
            var stored = Sides(row);
            var shown = target.SelectedSideItems ?? [];
            if (stored.Count != shown.Count || stored.Where((side, index) => side.Id != shown[index].Id
                || side.SuggestedSideItemId != shown[index].SuggestedSideItemId
                || side.ProductVariationId != shown[index].ProductVariationId
                || side.Quantity != shown[index].Quantity).Any()) throw Unsupported();
        }
    }

    private static void EnsureQuantities(BasketItem row)
    {
        if (string.IsNullOrWhiteSpace(row.IngredientQuantitiesJson)) return;
        try
        {
            var quantities = JsonSerializer.Deserialize<Dictionary<Guid, int>>(row.IngredientQuantitiesJson)
                ?? throw Unsupported();
            if (quantities.Any(pair => pair.Key == Guid.Empty || pair.Value < 0)) throw Unsupported();
        }
        catch (JsonException) { throw Unsupported(); }
    }

    private static List<SelectedSideItemDto> Sides(BasketItem row)
    {
        if (string.IsNullOrWhiteSpace(row.SelectedSideItemsJson)) return [];
        try
        {
            var sides = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(row.SelectedSideItemsJson)
                ?? throw Unsupported();
            if (sides.Any(side => side is null || side.Id == Guid.Empty || side.Quantity <= 0)) throw Unsupported();
            return sides;
        }
        catch (JsonException) { throw Unsupported(); }
    }

    private static ConflictException Unsupported() => new("This basket is too large or cannot be reviewed safely.");
}
