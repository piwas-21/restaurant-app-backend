using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundleByIdQuery;

/// <param name="RequestedOrderType">
/// The channel the guest is ordering through, or <c>null</c> when they have not chosen one. Drives
/// <see cref="MenuBundleDto.Availability"/> exactly as it does on the list query.
/// </param>
public record GetMenuBundleByIdQuery(Guid Id, OrderType? RequestedOrderType = null)
    : IQuery<ApiResponse<MenuBundleDto>>;

public class GetMenuBundleByIdQueryHandler(
    ApplicationDbContext context,
    IConfiguration configuration,
    ITenantClock clock,
    ICurrentUserService currentUser)
    : IQueryHandler<GetMenuBundleByIdQuery, ApiResponse<MenuBundleDto>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    private readonly ITenantClock _clock = clock;
    private readonly ICurrentUserService _currentUser = currentUser;

    public async Task<ApiResponse<MenuBundleDto>> Handle(GetMenuBundleByIdQuery query, CancellationToken cancellationToken)
    {
        var queryable = _context.Products
            // Load-bearing: inheritance resolves through the PRIMARY category and an unloaded
            // collection reads as UNRESTRICTED. See the same include on the list query.
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
            .Where(p => p.Id == query.Id && !p.IsDeleted);

        // #397: this read had NO schedule filter, so the guest list hid a bundle that this endpoint
        // then served in full — the featured-special "Details" sheet opens from here. The same
        // predicate the list uses, on the same clock, evaluated by the same engine.
        //
        // Staff are exempt, and deliberately: the bundle EDITOR loads through this endpoint
        // (`useProductEditorFetch`) and the till opens a bundle a guest asks for by name. Filtering
        // them would make an out-of-window lunch menu un-editable at 16:00 and un-sellable at the
        // counter. `IsStaff` is the shared dividing line, not a fresh per-handler predicate.
        // A machine API token counts as BACK-OF-HOUSE here, not as a guest — the rule, and why, is
        // written once on `ICurrentUserService.IsStaff`. (An earlier version of this comment said
        // the opposite. It was wrong about the code it sat on: `ApiTokenAuthenticationHandler`
        // stamps the token with the Admin role claim, so `IsStaff` has always been true for one.
        // `MenuBundleScheduleTests` now measures it instead of asserting it in prose.)
        if (!_currentUser.IsStaff)
        {
            var now = _clock.Now;
            queryable = queryable.Where(MenuScheduleWindow.AvailableAt(now.DayOfWeek, now.TimeOfDay));
        }

        var product = await queryable.FirstOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return ApiResponse<MenuBundleDto>.Failure("Menu bundle not found");
        }

        if (product.Type != ProductType.Menu)
        {
            return ApiResponse<MenuBundleDto>.Failure("Product is not a menu bundle");
        }

        var dto = MenuBundleMapper.MapToMenuBundleDto(product, _baseUrl, query.RequestedOrderType);
        return ApiResponse<MenuBundleDto>.SuccessWithData(dto);
    }
}
