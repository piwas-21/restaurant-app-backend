using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

internal static class BasketSelectedSideMapper
{
    public static async Task<Dictionary<Guid, List<BasketSideItemDto>>> MapAllAsync(
        ApplicationDbContext context, ILogger logger, IEnumerable<BasketItem> basketItems)
    {
        var items = Flatten(basketItems);
        var selectionsByItemId = new Dictionary<Guid, List<SelectedSideItemDto>>();
        var sideProductIds = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SelectedSideItemsJson)) continue;
            try
            {
                var selections = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(item.SelectedSideItemsJson);
                if (selections is not { Count: > 0 }) continue;
                selectionsByItemId[item.Id] = selections;
                foreach (var selection in selections)
                    sideProductIds.Add(selection.Id);
            }
            catch (JsonException exception)
            {
                logger.LogWarning(exception,
                    "Failed to deserialize side items JSON for basket item {BasketItemId}", item.Id);
            }
        }

        if (sideProductIds.Count == 0) return [];
        var products = await context.Products.AsNoTracking()
            .Include(product => product.Variations)
            .Where(product => sideProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);
        var resultByItemId = new Dictionary<Guid, List<BasketSideItemDto>>(selectionsByItemId.Count);
        foreach (var (basketItemId, selections) in selectionsByItemId)
        {
            var result = new List<BasketSideItemDto>(selections.Count);
            foreach (var selection in selections)
            {
                if (!products.TryGetValue(selection.Id, out var product)) continue;
                var variation = selection.ProductVariationId.HasValue
                    ? product.Variations.FirstOrDefault(row => row.Id == selection.ProductVariationId.Value)
                    : null;
                if (selection.ProductVariationId.HasValue && variation is null)
                    throw new BadRequestException("A selected basket-side variation is no longer available.");
                var price = product.BasePrice + (variation?.PriceModifier ?? 0m);
                result.Add(new BasketSideItemDto
                {
                    Id = product.Id,
                    SuggestedSideItemId = selection.SuggestedSideItemId,
                    Name = product.Name,
                    Description = product.Description,
                    Price = price,
                    ImageUrl = product.ImageUrl,
                    ProductVariationId = variation?.Id,
                    VariationName = variation?.Name,
                    Quantity = selection.Quantity,
                    PresentationOrder = selection.PresentationOrder,
                    CompositionRole = selection.CompositionRole,
                    SubTotal = price * selection.Quantity
                });
            }

            resultByItemId[basketItemId] = result;
        }

        return resultByItemId;
    }

    private static List<BasketItem> Flatten(IEnumerable<BasketItem> roots)
    {
        var items = new List<BasketItem>();
        var visited = new HashSet<Guid>();
        var pending = new Queue<BasketItem>(roots);
        while (pending.TryDequeue(out var item))
        {
            if (!visited.Add(item.Id)) continue;
            items.Add(item);
            foreach (var child in item.ChildBasketItems)
                pending.Enqueue(child);
        }

        return items;
    }
}
