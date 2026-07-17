using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus.Queries.GetMenuBundleByIdQuery;

public record GetMenuBundleByIdQuery(Guid Id) : IQuery<ApiResponse<MenuBundleDto>>;

public class GetMenuBundleByIdQueryHandler(ApplicationDbContext context, IConfiguration configuration)
    : IQueryHandler<GetMenuBundleByIdQuery, ApiResponse<MenuBundleDto>>
{
    private readonly ApplicationDbContext _context = context;
    private readonly string _baseUrl = configuration["AWS:S3:BaseUrl"]!;

    public async Task<ApiResponse<MenuBundleDto>> Handle(GetMenuBundleByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await _context.Products
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

        var dto = MenuBundleMapper.MapToMenuBundleDto(product, _baseUrl);
        return ApiResponse<MenuBundleDto>.SuccessWithData(dto);
    }
}
