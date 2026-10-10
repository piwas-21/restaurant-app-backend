using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public partial class OrderItemFactory
{
    private async Task<Guid?> ResolveChoiceSectionAsync(
        OrderItem? parent, CreateOrderItemDto item, CancellationToken cancellationToken)
    {
        if (parent?.ProductId is null || item.Kind != OrderItemKind.BundleChild)
            return null;

        // Resolve membership from identities, never names. A stable row ID wins and can describe a
        // component that picked one of its own variations; the older pair of product + fixed row
        // variation remains supported only when it resolves unambiguously.
        if (item.MenuSectionItemId.HasValue)
        {
            var row = await _context.MenuSectionItems.AsNoTracking()
                .Where(option => option.Id == item.MenuSectionItemId.Value
                    && option.MenuSection.MenuDefinition.ProductId == parent.ProductId
                    && option.ProductId == item.ProductId
                    && (!item.SectionId.HasValue || option.MenuSectionId == item.SectionId))
                .Select(option => new { option.MenuSectionId, option.ProductVariationId })
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null || (row.ProductVariationId.HasValue
                && row.ProductVariationId != item.ProductVariationId))
                throw new BadRequestException("The selected choice section does not contain this bundle component.");
            return row.MenuSectionId;
        }

        var sections = await _context.MenuSectionItems.AsNoTracking()
            .Where(option => option.MenuSection.MenuDefinition.ProductId == parent.ProductId
                && option.ProductId == item.ProductId
                && option.ProductVariationId == item.ProductVariationId
                && (!item.SectionId.HasValue || option.MenuSectionId == item.SectionId))
            .Select(option => option.MenuSectionId).Distinct().Take(2)
            .ToListAsync(cancellationToken);
        if (item.SectionId.HasValue && sections.Count == 0)
            throw new BadRequestException("The selected choice section does not contain this bundle component.");
        return sections.Count == 1 ? sections[0] : null;
    }
}
