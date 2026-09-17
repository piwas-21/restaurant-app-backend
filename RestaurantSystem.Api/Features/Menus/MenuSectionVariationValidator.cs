using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// Validates variation references on menu section items before a bundle replace is persisted.
/// Foreign keys prove that an id exists, but they cannot prove that it belongs to the selected
/// option product, is live, or is orderable.
/// </summary>
public static class MenuSectionVariationValidator
{
    public static async Task ValidateAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSectionDto> sections,
        CancellationToken cancellationToken)
    {
        var references = sections
            .SelectMany(section => section.Items ?? [])
            .Where(item => item.ProductVariationId.HasValue)
            .Select(item => (item.ProductId, VariationId: item.ProductVariationId!.Value))
            .ToList();

        if (references.Count == 0)
        {
            return;
        }

        var variationIds = references.Select(reference => reference.VariationId).Distinct().ToList();
        var variations = await context.ProductVariations
            .Where(variation => variationIds.Contains(variation.Id) && !variation.IsDeleted)
            .Select(variation => new { variation.Id, variation.ProductId, variation.IsActive })
            .ToDictionaryAsync(variation => variation.Id, cancellationToken);

        foreach (var (productId, variationId) in references)
        {
            if (!variations.TryGetValue(variationId, out var variation))
            {
                throw new BadRequestException($"Menu section variation '{variationId}' was not found");
            }

            if (variation.ProductId != productId)
            {
                throw new BadRequestException(
                    $"Menu section variation '{variationId}' does not belong to product '{productId}'");
            }

            if (!variation.IsActive)
            {
                throw new BadRequestException($"Menu section variation '{variationId}' is not active");
            }
        }
    }
}
