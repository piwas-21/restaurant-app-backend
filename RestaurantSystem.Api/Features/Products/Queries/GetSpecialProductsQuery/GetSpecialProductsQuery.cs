using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetSpecialProductsQuery;

/// <summary>
/// Query to get all products marked as special (IsSpecial = true)
/// </summary>
/// <param name="RequestedOrderType">
/// The channel the guest is browsing on, or <c>null</c> when they have not chosen one.
/// </param>
public record GetSpecialProductsQuery(
    int Page = 1,
    int PageSize = 20,
    OrderType? RequestedOrderType = null
) : IQuery<ApiResponse<PagedResult<SpecialProductDto>>>;

public class GetSpecialProductsQueryHandler : IQueryHandler<GetSpecialProductsQuery, ApiResponse<PagedResult<SpecialProductDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetSpecialProductsQueryHandler> _logger;
    private readonly string _baseUrl;

    public GetSpecialProductsQueryHandler(
        ApplicationDbContext context,
        ILogger<GetSpecialProductsQueryHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<PagedResult<SpecialProductDto>>> Handle(
        GetSpecialProductsQuery query,
        CancellationToken cancellationToken)
    {
        // Query all products where IsSpecial = true
        var specialProductsQuery = _context.Products
            // See GetFeaturedSpecialQuery: without the inheritance chain an inheriting product
            // resolves as UNRESTRICTED, so a restricted special would advertise itself as orderable.
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Images)
            // Split: 2+ collection Includes over MANY roots multiply rows (S8733). Placed in
            // THIS chain, beside the Includes it is about, rather than on the materialising
            // statement below — behaviour is identical (it is query metadata, not a positional
            // operator) but Sonar's rule is syntactic and does not follow the variable.
            .AsSplitQuery()
            // IsActive as well as IsSpecial, matching GetFeaturedSpecialQuery's
            // `p.IsFeaturedSpecial && p.IsSpecial && p.IsActive`. The two queries answer ONE
            // question — "which specials are there?" — and disagreed about a deactivated one, on an
            // endpoint that is [AllowAnonymous]. Two consequences, and the second is a real defect
            // rather than a tidiness argument:
            //
            //   - anonymously, `GET /api/Products/specials` served products the restaurant had
            //     switched OFF. No shipped guest surface reads it today (its only caller is
            //     /admin/specials-management, behind AdminAuthGuard), so this is closing a latent
            //     hole, not a live leak — but the endpoint is anonymous, so "no caller" is a fact
            //     about our clients, not a guarantee.
            //   - the admin specials table offered `Set Featured` on a deactivated special, and
            //     featuring one is a SILENT NO-OP: GetFeaturedSpecialQuery filters IsActive, so the
            //     banner never renders it. The admin pressed a button that could not work. Dropping
            //     inactive rows from this list removes the action along with the row.
            //
            // The admin loses nothing it can act on: this page's only control is `Set Featured`
            // (itself gated on IsAvailable), its Status column reads IsAvailable and never IsActive,
            // and un-marking an item as special is done in Menu Management — which is what this
            // table's own empty state already tells the admin.
            .Where(p => p.IsSpecial && p.IsActive)
            .AsQueryable();

        // Get total count
        var totalCount = await specialProductsQuery.CountAsync(cancellationToken);

        // Get paginated products
        var products = await specialProductsQuery
            // See GetProductsQuery: the split above and ThenBy(Id) travel together under Skip/Take.
            .OrderByDescending(p => p.IsFeaturedSpecial) // Featured first
            .ThenBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var productDtos = products.Select(p => new SpecialProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            BasePrice = p.BasePrice,
            ImageUrl = p.Images
                .Where(img => img.IsPrimary && !string.IsNullOrEmpty(img.Url))
                .Select(img => UrlJoin.Join(_baseUrl, img.Url))
                .FirstOrDefault() ?? p.ImageUrl,
            Availability = OrderTypeAvailability.Resolve(p, query.RequestedOrderType),
            IsActive = p.IsActive,
            IsAvailable = p.IsAvailable,
            IsSpecial = p.IsSpecial,
            IsFeaturedSpecial = p.IsFeaturedSpecial,
            FeaturedDate = p.FeaturedDate,
            DisplayOrder = p.DisplayOrder
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<SpecialProductDto>(
            productDtos,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        _logger.LogInformation(
            "Retrieved {ProductCount} special products (page {Page} of {TotalPages})",
            products.Count, query.Page, totalPages);

        return ApiResponse<PagedResult<SpecialProductDto>>.SuccessWithData(
            result,
            $"Retrieved {products.Count} special products");
    }
}
