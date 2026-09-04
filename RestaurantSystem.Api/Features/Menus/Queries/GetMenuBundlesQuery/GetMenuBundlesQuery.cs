using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundlesQuery;

/// <param name="RequestedOrderType">
/// The channel the guest is ordering through. Does NOT filter the list — a blocked bundle stays
/// visible with a reason, exactly as products do (ORDER-TYPE-AVAILABILITY-PLAN §4.4). It only
/// resolves each row's <see cref="MenuBundleDto.Availability"/>.
/// </param>
public record GetMenuBundlesQuery(
    int Page,
    int PageSize,
    Guid? CategoryId = null,
    bool IncludeUnavailable = false,
    OrderType? RequestedOrderType = null) : IQuery<ApiResponse<PagedResult<MenuBundleDto>>>;

public class GetMenuBundlesQueryHandler(
    ApplicationDbContext context,
    IConfiguration configuration,
    ITenantClock clock)
    : IQueryHandler<GetMenuBundlesQuery, ApiResponse<PagedResult<MenuBundleDto>>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    private readonly ITenantClock _clock = clock;
    // The original _logger field and its injection via the constructor are removed as per the primary constructor syntax in the provided change.

    public async Task<ApiResponse<PagedResult<MenuBundleDto>>> Handle(GetMenuBundlesQuery query, CancellationToken cancellationToken)
    {
        var queryable = _context.Products
            // ProductCategories -> Category is load-bearing, not cosmetic: a bundle with no mask of
            // its own inherits its PRIMARY category's, and an unloaded collection reads as
            // UNRESTRICTED. Omit this include and every restricted bundle reports as orderable,
            // silently — no exception, no empty field.
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.MenuDefinition)
                .ThenInclude(md => md!.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
                            .ThenInclude(p => p.DetailedIngredients)
                                .ThenInclude(di => di.Descriptions)
            .Include(p => p.Descriptions)
            .Include(p => p.Images)
            // Split: 2+ collection Includes over MANY roots multiply rows (S8733). Placed in
            // THIS chain, beside the Includes it is about, rather than on the materialising
            // statement below — behaviour is identical (it is query metadata, not a positional
            // operator) but Sonar's rule is syntactic and does not follow the variable.
            .AsSplitQuery()
            .Where(p => !p.IsDeleted && p.MenuDefinition != null);

        // Filter by schedule availability (only if not including unavailable)
        if (!query.IncludeUnavailable)
        {
            // The TENANT's wall clock, not the container's UTC (#397). `_clock.Now` carries the
            // tenant offset, so `.DayOfWeek`/`.TimeOfDay` are the day and time on the restaurant's
            // own wall — the same two values `WorkingHoursService.IsOpenNowAsync` reads.
            var now = _clock.Now;

            queryable = queryable.Where(MenuScheduleWindow.AvailableAt(now.DayOfWeek, now.TimeOfDay));
        }


        var totalCount = await queryable.CountAsync(cancellationToken);

        var products = await queryable
            // See GetProductsQuery: the split above and ThenBy(Id) travel together under Skip/Take.
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = products
            .Select(p => MenuBundleMapper.MapToMenuBundleDto(p, _baseUrl, query.RequestedOrderType))
            .ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<MenuBundleDto>(
            dtos,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        return ApiResponse<PagedResult<MenuBundleDto>>.SuccessWithData(result,
            $"Retrieved {products.Count} menu bundles");
    }
}
