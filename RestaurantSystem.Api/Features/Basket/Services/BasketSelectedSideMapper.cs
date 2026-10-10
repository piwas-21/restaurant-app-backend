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
        var selectionsByItemId = DeserializeSelections(Flatten(basketItems), logger);
        var productIds = selectionsByItemId.Values.SelectMany(selections => selections)
            .Select(selection => selection.Id).Distinct().ToList();
        if (productIds.Count == 0) return [];

        var products = await context.Products.AsNoTracking()
            .Include(product => product.Variations)
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);
        return MapSelections(selectionsByItemId, products);
    }

    private static Dictionary<Guid, List<SelectedSideItemDto>> DeserializeSelections(
        IEnumerable<BasketItem> items, ILogger logger)
    {
        var selectionsByItemId = new Dictionary<Guid, List<SelectedSideItemDto>>();
        foreach (var item in items)
            TryAddSelections(item, selectionsByItemId, logger);
        return selectionsByItemId;
    }

    private static void TryAddSelections(
        BasketItem item, Dictionary<Guid, List<SelectedSideItemDto>> selectionsByItemId, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(item.SelectedSideItemsJson)) return;
        try
        {
            var selections = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(item.SelectedSideItemsJson);
            if (selections is { Count: > 0 })
                selectionsByItemId[item.Id] = selections;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception,
                "Failed to deserialize side items JSON for basket item {BasketItemId}", item.Id);
        }
    }

    private static Dictionary<Guid, List<BasketSideItemDto>> MapSelections(
        Dictionary<Guid, List<SelectedSideItemDto>> selectionsByItemId,
        Dictionary<Guid, Product> products)
    {
        var result = new Dictionary<Guid, List<BasketSideItemDto>>(selectionsByItemId.Count);
        foreach (var (basketItemId, selections) in selectionsByItemId)
            result[basketItemId] = selections
                .Where(selection => products.ContainsKey(selection.Id))
                .Select(selection => MapSelection(selection, products[selection.Id]))
                .ToList();
        return result;
    }

    private static BasketSideItemDto MapSelection(SelectedSideItemDto selection, Product product)
    {
        var variation = selection.ProductVariationId.HasValue
            ? product.Variations.FirstOrDefault(row => row.Id == selection.ProductVariationId.Value)
            : null;
        if (selection.ProductVariationId.HasValue && variation is null)
            throw new BadRequestException("A selected basket-side variation is no longer available.");
        var price = product.BasePrice + (variation?.PriceModifier ?? 0m);
        return new BasketSideItemDto
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
        };
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
