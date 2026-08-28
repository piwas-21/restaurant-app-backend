using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientProductsQuery;

public record GetGlobalIngredientProductsQuery(Guid Id)
    : IQuery<ApiResponse<List<CatalogUsageProductDto>>>;

/// <summary>
/// Which products carry a copy of one library row — the drill-down behind S3's "used on N items",
/// and the half a blast-radius confirm cannot work without (plan D6, slice S8).
/// </summary>
/// <remarks>
/// <para>
/// <b>It answers the same question as <c>GlobalIngredientUsage.CountForAsync</c>, and it must keep
/// answering it the same way</b> — counted through <c>Products</c> so the soft-delete filter
/// applies, DISTINCT by product because one product may carry two rows copied from the same library
/// entry, and inactive products included. <c>UsageListAndCount_AgreeOnTheSameSet</c> pins that
/// agreement; a screen that says "used on 3 items" above a list of 4 has one of them wrong.
/// </para>
/// <para>
/// ONE query, ordered by name so the confirm dialog reads the same way twice.
/// </para>
/// </remarks>
public class GetGlobalIngredientProductsQueryHandler
    : IQueryHandler<GetGlobalIngredientProductsQuery, ApiResponse<List<CatalogUsageProductDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetGlobalIngredientProductsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<CatalogUsageProductDto>>> Handle(
        GetGlobalIngredientProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = await _context.Products
            .Where(p => p.DetailedIngredients.Any(i => i.GlobalIngredientId == query.Id))
            .OrderBy(p => p.Name)
            .Select(p => new CatalogUsageProductDto
            {
                ProductId = p.Id,
                ProductName = p.Name,
                IsActive = p.IsActive,
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<List<CatalogUsageProductDto>>.SuccessWithData(products);
    }
}
