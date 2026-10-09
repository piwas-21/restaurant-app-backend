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

        // Resolve membership from identities, never names. For an old producer without a section
        // id, annotate only an unambiguous membership; explicitly supplied wrong membership fails.
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
