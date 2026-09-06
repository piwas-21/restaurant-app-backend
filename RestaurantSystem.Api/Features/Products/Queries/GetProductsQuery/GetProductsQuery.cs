using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;

public record GetProductsQuery(
    Guid? CategoryId,
    ProductType? Type,
    ProductType? ExcludeType,
    // Honoured for BACK-OF-HOUSE callers only. A guest never sees a deactivated product, whatever
    // this says — see the handler, and #438 for the five-caller table that decided it.
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
    // Opt in to COMPONENT products (`Product.IsComponent`) — items that exist only to be chosen
    // inside a bundle section and are not catalogue items. Without it they are excluded, which is
    // what every guest surface wants; the admin menu list and the bundle option picker are the two
    // callers that need them. Shaped exactly like IncludeMenus above, and binary for the same
    // reason: false and "omitted" mean the same thing.
    //
    // Unlike IncludeMenus this exclusion has NO explicit-filter escape hatch, because no filter
    // names components — so the opt-in is the only way to see one in a list. A single component is
    // still readable by id (`GET /api/Products/{id}`) on purpose: the admin editor must open one.
    bool IncludeComponents = false,
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
    private readonly ICurrentUserService _currentUser;
    private readonly string _baseUrl;

    public GetProductsQueryHandler(
        ApplicationDbContext context,
        ILogger<GetProductsQueryHandler> logger,
        ICurrentUserService currentUser,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _currentUser = currentUser;
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
            // Split: 2+ collection Includes over MANY roots multiply rows (S8733). Placed in
            // THIS chain, beside the Includes it is about, rather than on the materialising
            // statement below — behaviour is identical (it is query metadata, not a positional
            // operator) but Sonar's rule is syntactic and does not follow the variable.
            .AsSplitQuery()
            // Pure read → DTO projection; change tracking would only cost allocations on the
            // hottest guest endpoint. Nothing downstream of this query writes.
            .AsNoTracking()
            .AsQueryable();

        // Row-level filters (category/bundle/component/active/available/special/search + the
        // guest ALL-view hidden-category exclusion), in one place: ProductListFilters.
        productsQuery = await ProductListFilters.Apply(
            _context, query, _currentUser, productsQuery, cancellationToken);


        // Get total count
        var totalCount = await productsQuery.CountAsync(cancellationToken);


        // The guest's ALL view (no CategoryId) reads as one menu, so it follows the menu's own
        // structure — primary category display_order, then the product's own (partner feedback
        // 2026-09-06). Staff keeps the flat order the admin list edits against; the IsActive
        // branch above already splits guest/staff on purpose. Policy + rationale:
        // ProductListOrdering. The shared ThenBy tail is REQUIRED by the split query: Skip/Take
        // correlates its round-trips by the ordering, so a non-unique one can attach one
        // product's images to another.
        var ordered = (query.CategoryId is null && !_currentUser.IsStaff
                ? ProductListOrdering.ForGuestAllView(productsQuery)
                : ProductListOrdering.Flat(productsQuery))
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id);

        var products = await ordered
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
