using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Queries.SearchGlobalIngredientsQuery;

public record SearchGlobalIngredientsQuery(string? Query, int Limit = 10) : IQuery<ApiResponse<List<GlobalIngredientDto>>>;

public class SearchGlobalIngredientsQueryHandler : IQueryHandler<SearchGlobalIngredientsQuery, ApiResponse<List<GlobalIngredientDto>>>
{
    private readonly ApplicationDbContext _context;

    public SearchGlobalIngredientsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<GlobalIngredientDto>>> Handle(SearchGlobalIngredientsQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return ApiResponse<List<GlobalIngredientDto>>.SuccessWithData([]);
        }

        var normalizedQuery = query.Query.Trim().ToLower();

        var ingredients = await _context.GlobalIngredients
            .Include(g => g.Translations)
            .Where(g => g.IsActive && g.DefaultName.ToLower().Contains(normalizedQuery))
            // Prioritize starts-with matches
            .OrderBy(g => g.DefaultName.ToLower().StartsWith(normalizedQuery) ? 0 : 1)
            .ThenBy(g => g.DefaultName)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);

        var dtos = ingredients.Select(GlobalIngredientMapper.ToDto).ToList();
        return ApiResponse<List<GlobalIngredientDto>>.SuccessWithData(dtos);
    }
}
