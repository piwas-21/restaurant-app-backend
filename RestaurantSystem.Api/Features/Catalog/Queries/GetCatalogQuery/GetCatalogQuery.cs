using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalog.Queries.GetCatalogQuery;

public sealed record GetCatalogQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? CategoryId = null,
    OrderType? RequestedOrderType = null) : IQuery<ApiResponse<PagedResult<CatalogOfferFamilyDto>>>;

public sealed class GetCatalogQueryHandler(
    ApplicationDbContext context,
    IConfiguration configuration,
    ITenantClock clock)
    : IQueryHandler<GetCatalogQuery, ApiResponse<PagedResult<CatalogOfferFamilyDto>>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"] ?? string.Empty;
    private readonly ITenantClock _clock = clock;

    public async Task<ApiResponse<PagedResult<CatalogOfferFamilyDto>>> Handle(
        GetCatalogQuery query,
        CancellationToken cancellationToken)
    {
        var products = await _context.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Images.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            .Include(p => p.Descriptions)
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Variations.Where(v => !v.IsDeleted && v.IsActive).OrderBy(v => v.DisplayOrder))
                .ThenInclude(v => v.Descriptions)
            .Include(p => p.MenuDefinition)
            // Keep inactive anchors when they own linked menu offers so the builder can render a
            // single, disabled family card with the still-usable alternatives. Inactive
            // standalone products and inactive child menus remain excluded by the builder.
            .Where(p => !p.IsDeleted && !p.IsComponent
                && (p.IsActive
                    || _context.MenuDefinitions.Any(definition =>
                        definition.ParentOfferProductId == p.Id)))
            .Take(CatalogQueryLimits.MaxProducts + 1)
            .ToListAsync(cancellationToken);

        if (products.Count > CatalogQueryLimits.MaxProducts)
        {
            throw new BadRequestException(
                $"Catalogue exceeds the supported limit of {CatalogQueryLimits.MaxProducts} products");
        }

        var now = _clock.Now;
        var families = CatalogOfferFamilyBuilder.Build(
            products,
            query.CategoryId,
            query.RequestedOrderType,
            now.DayOfWeek,
            now.TimeOfDay,
            _baseUrl);

        var page = Math.Max(1, query.Page);
        // The one-page guest catalogue requests 200 rows. Keep a bounded server-side guard while
        // matching that established page size; a future virtualized client can simply request
        // smaller pages without changing the family contract.
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var totalPages = (int)Math.Ceiling(families.Count / (double)pageSize);
        var items = families
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return ApiResponse<PagedResult<CatalogOfferFamilyDto>>.SuccessWithData(
            new PagedResult<CatalogOfferFamilyDto>(items, families.Count, page, pageSize, totalPages),
            $"Retrieved {items.Count} catalogue offer families");
    }
}
