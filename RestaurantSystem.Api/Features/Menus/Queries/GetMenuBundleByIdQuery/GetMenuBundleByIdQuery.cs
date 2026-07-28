using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
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

public class GetMenuBundleByIdQueryHandler(ApplicationDbContext context, IConfiguration configuration)
    : IQueryHandler<GetMenuBundleByIdQuery, ApiResponse<MenuBundleDto>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;

    public async Task<ApiResponse<MenuBundleDto>> Handle(GetMenuBundleByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await _context.Products
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
            .FirstOrDefaultAsync(p => p.Id == query.Id && !p.IsDeleted, cancellationToken);

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
