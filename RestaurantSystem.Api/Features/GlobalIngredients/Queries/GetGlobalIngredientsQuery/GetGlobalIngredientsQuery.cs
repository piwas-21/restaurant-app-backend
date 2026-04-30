using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientsQuery;

public record GetGlobalIngredientsQuery : IQuery<ApiResponse<List<GlobalIngredientDto>>>;

public class GetGlobalIngredientsQueryHandler : IQueryHandler<GetGlobalIngredientsQuery, ApiResponse<List<GlobalIngredientDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetGlobalIngredientsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<GlobalIngredientDto>>> Handle(GetGlobalIngredientsQuery query, CancellationToken cancellationToken)
    {
        var ingredients = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .Where(g => g.IsActive)
            .OrderBy(g => g.DefaultName)
            .ToListAsync(cancellationToken);

        var dtos = ingredients.Select(GlobalIngredientMapper.ToDto).ToList();
        return ApiResponse<List<GlobalIngredientDto>>.SuccessWithData(dtos);
    }
}
