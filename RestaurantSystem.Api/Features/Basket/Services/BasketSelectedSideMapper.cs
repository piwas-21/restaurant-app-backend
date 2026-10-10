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
    public static async Task<List<BasketSideItemDto>?> MapAsync(
        ApplicationDbContext context, ILogger logger, BasketItem item)
    {
        if (string.IsNullOrWhiteSpace(item.SelectedSideItemsJson)) return null;
        List<SelectedSideItemDto>? selections;
        try
        {
            selections = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(item.SelectedSideItemsJson);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Failed to deserialize side items JSON for basket item {BasketItemId}", item.Id);
            return null;
        }

        if (selections is not { Count: > 0 }) return null;
        var ids = selections.Select(selection => selection.Id).Distinct().ToList();
        var products = await context.Products.AsNoTracking()
            .Include(product => product.Variations)
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);
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

        return result;
    }
}
