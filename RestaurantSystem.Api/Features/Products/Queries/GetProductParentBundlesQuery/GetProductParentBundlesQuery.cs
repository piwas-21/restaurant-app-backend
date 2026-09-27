using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductParentBundlesQuery;

public sealed record GetProductParentBundlesQuery(Guid ProductId)
    : IQuery<ApiResponse<ProductParentBundlesDto>>;

public sealed class GetProductParentBundlesQueryHandler
    : IQueryHandler<GetProductParentBundlesQuery, ApiResponse<ProductParentBundlesDto>>
{
    private readonly ApplicationDbContext _context;

    public GetProductParentBundlesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ApiResponse<ProductParentBundlesDto>> Handle(
        GetProductParentBundlesQuery query,
        CancellationToken cancellationToken)
    {
        var productExists = await _context.Products
            .AnyAsync(product => product.Id == query.ProductId, cancellationToken);
        if (!productExists)
        {
            throw new NotFoundException("Product", query.ProductId);
        }

        var rows = await _context.MenuSectionItems
            .AsNoTracking()
            .Where(sectionItem => sectionItem.ProductId == query.ProductId
                && sectionItem.MenuSection.MenuDefinition.Product.Type == ProductType.Menu)
            .Select(sectionItem => new ParentBundleReferenceRow(
                sectionItem.MenuSection.MenuDefinition.ProductId,
                sectionItem.MenuSection.MenuDefinition.Product.Name,
                sectionItem.MenuSection.MenuDefinition.Product.IsActive,
                sectionItem.MenuSectionId,
                sectionItem.ProductVariationId))
            .ToListAsync(cancellationToken);

        var items = rows
            .GroupBy(row => new { row.BundleId, row.Name, row.IsActive })
            .OrderBy(group => group.Key.Name)
            .ThenBy(group => group.Key.BundleId)
            .Select(group => new ProductParentBundleDto
            {
                Id = group.Key.BundleId,
                Name = group.Key.Name,
                IsActive = group.Key.IsActive,
                References = group
                    .Select(row => new ProductParentBundleReferenceDto
                    {
                        SectionId = row.SectionId,
                        ProductVariationId = row.ProductVariationId
                    })
                    .DistinctBy(reference => (reference.SectionId, reference.ProductVariationId))
                    .OrderBy(reference => reference.SectionId)
                    .ThenBy(reference => reference.ProductVariationId)
                    .ToList()
            })
            .ToList();

        return ApiResponse<ProductParentBundlesDto>.SuccessWithData(
            new ProductParentBundlesDto { Items = items });
    }

    private sealed record ParentBundleReferenceRow(
        Guid BundleId,
        string Name,
        bool IsActive,
        Guid SectionId,
        Guid? ProductVariationId);
}
