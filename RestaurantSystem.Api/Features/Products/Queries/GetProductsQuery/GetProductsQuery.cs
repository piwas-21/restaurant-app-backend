using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;

public record GetProductsQuery(
    Guid? CategoryId,
    ProductType? Type,
    ProductType? ExcludeType,
    bool? IsActive,
    bool? IsAvailable,
    bool? isSpeacial,
    string? Search,
    int Page = 1,
    int PageSize = 20,
    // Opt in to a mixed list of items AND Menu bundles. Without it an unfiltered
    // query hides Menu bundles, which is what the customer catalog wants but
    // leaves the admin unable to page one list over both (redesign #176).
    // Only skips the default exclusion — it never widens an explicit filter, so
    // both Type and ExcludeType still win over it. Non-nullable unlike the sibling
    // filters: those are tri-state (null = don't filter on it), this is a binary
    // opt-in where false and "omitted" mean the same thing.
    bool IncludeMenus = false,
    // The channel the guest is ordering through. Does NOT filter the list — blocked items stay
    // visible so the customer sees "Dürüm is takeaway & delivery only" instead of a hole in the
    // menu. It only resolves each row's `Availability`. Null (no type chosen yet, the dominant
    // browse state) reports everything as orderable and still fills AllowedOrderTypes for the chip.
    OrderType? RequestedOrderType = null
) : IQuery<ApiResponse<PagedResult<ProductSummaryDto>>>;

public class GetProductsQueryHandler : IQueryHandler<GetProductsQuery, ApiResponse<PagedResult<ProductSummaryDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetProductsQueryHandler> _logger;
    private readonly string _baseUrl;

    public GetProductsQueryHandler(ApplicationDbContext context, ILogger<GetProductsQueryHandler> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<PagedResult<ProductSummaryDto>>> Handle(GetProductsQuery query, CancellationToken cancellationToken)
    {
        var productsQuery = _context.Products
            .Include(p => p.Images)
            .Include(p => p.Descriptions)
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Variations.Where(v => v.IsActive))
                .ThenInclude(v => v.Descriptions)
            .AsQueryable();

        // Apply filters
        if (query.CategoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == query.CategoryId.Value));
        }

        if (query.Type.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Type == query.Type.Value);
        }
        else if (!query.IncludeMenus)
        {
            // Default behavior: Exclude Menu bundles unless specifically requested via Type
            // or opted into via IncludeMenus.
            productsQuery = productsQuery.Where(p => p.Type != ProductType.Menu);
        }

        if (query.ExcludeType.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Type != query.ExcludeType.Value);
        }

        if (query.IsActive.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsActive == query.IsActive.Value);
        }

        if (query.IsAvailable.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsAvailable == query.IsAvailable.Value);
        }

        if (query.isSpeacial.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsSpecial == query.isSpeacial.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchLower = query.Search.ToLower();

            productsQuery = productsQuery.Where(p => p.Name.ToLower().Contains(searchLower) || p.Descriptions.Any(c => c.Name.ToLower().Contains(searchLower)));
        }


        // Get total count
        var totalCount = await productsQuery.CountAsync(cancellationToken);


        var products = await productsQuery
        // 2+ collection Includes over many roots multiply rows (S8733). ThenBy(Id) is
        // REQUIRED alongside it, not cosmetic: a split query with Skip/Take correlates its
        // separate round-trips by the ordering, so a non-unique one (DisplayOrder+Name is
        // not unique) can attach a product's images to a different product.
        .AsSplitQuery()
        .OrderBy(p => p.DisplayOrder)
        .ThenBy(p => p.Name)
        .ThenBy(p => p.Id)
        .Skip((query.Page - 1) * query.PageSize)
        .Take(query.PageSize)
        .ToListAsync(cancellationToken);

        var productDtos = products
            .Select(p => ProductSummaryMapper.MapToSummaryDto(p, _baseUrl, query.RequestedOrderType))
            .ToList();



        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<ProductSummaryDto>(
            productDtos,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        _logger.LogInformation("Retrieved {ProductCount} products (page {Page} of {TotalPages})",
            products.Count, query.Page, totalPages);

        return ApiResponse<PagedResult<ProductSummaryDto>>.SuccessWithData(result,
            $"Retrieved {products.Count} products");
    }
}
