using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundlesQuery;

public record GetMenuBundlesQuery(int Page, int PageSize, Guid? CategoryId = null, bool IncludeUnavailable = false) : IQuery<ApiResponse<PagedResult<MenuBundleDto>>>;

public class GetMenuBundlesQueryHandler(ApplicationDbContext context, IConfiguration configuration)
    : IQueryHandler<GetMenuBundlesQuery, ApiResponse<PagedResult<MenuBundleDto>>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    // The original _logger field and its injection via the constructor are removed as per the primary constructor syntax in the provided change.

    public async Task<ApiResponse<PagedResult<MenuBundleDto>>> Handle(GetMenuBundlesQuery query, CancellationToken cancellationToken)
    {
        var queryable = _context.Products
            .Include(p => p.MenuDefinition)
                .ThenInclude(md => md!.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
                            .ThenInclude(p => p.DetailedIngredients)
                                .ThenInclude(di => di.Descriptions)
            .Include(p => p.Descriptions)
            .Include(p => p.Images)
            .Where(p => !p.IsDeleted && p.MenuDefinition != null);

        // Filter by schedule availability (only if not including unavailable)
        if (!query.IncludeUnavailable)
        {
            var now = DateTime.UtcNow;
            var currentDayOfWeek = now.DayOfWeek;
            var currentTime = now.TimeOfDay;

            queryable = queryable.Where(p =>
                p.MenuDefinition!.IsAlwaysAvailable || // Include if always available
                (
                    // Check if available on current day
                    (currentDayOfWeek == DayOfWeek.Monday && p.MenuDefinition.AvailableMonday) ||
                    (currentDayOfWeek == DayOfWeek.Tuesday && p.MenuDefinition.AvailableTuesday) ||
                    (currentDayOfWeek == DayOfWeek.Wednesday && p.MenuDefinition.AvailableWednesday) ||
                    (currentDayOfWeek == DayOfWeek.Thursday && p.MenuDefinition.AvailableThursday) ||
                    (currentDayOfWeek == DayOfWeek.Friday && p.MenuDefinition.AvailableFriday) ||
                    (currentDayOfWeek == DayOfWeek.Saturday && p.MenuDefinition.AvailableSaturday) ||
                    (currentDayOfWeek == DayOfWeek.Sunday && p.MenuDefinition.AvailableSunday)
                ) &&
                (
                    // Check if within time range (if times are set)
                    (p.MenuDefinition.StartTime == null && p.MenuDefinition.EndTime == null) ||
                    (p.MenuDefinition.StartTime != null && p.MenuDefinition.EndTime != null &&
                     currentTime >= p.MenuDefinition.StartTime && currentTime <= p.MenuDefinition.EndTime)
                )
            );
        }


        var totalCount = await queryable.CountAsync(cancellationToken);

        var products = await queryable
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var dtos = products.Select(p => MenuBundleMapper.MapToMenuBundleDto(p, _baseUrl)).ToList();

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
