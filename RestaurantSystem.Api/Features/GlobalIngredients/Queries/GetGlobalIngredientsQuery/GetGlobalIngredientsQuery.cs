using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.GlobalIngredients.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Queries.GetGlobalIngredientsQuery;

/// <param name="ArchivedOnly">
/// The archive drawer, rather than the shelf. One query serves both because everything else about
/// them — the includes, the usage count, the projection — is identical, and a second handler would
/// have been the same 30 lines with one predicate flipped.
/// </param>
public record GetGlobalIngredientsQuery(bool ArchivedOnly = false) : IQuery<ApiResponse<List<GlobalIngredientDto>>>;

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
            // The shelf is active AND not archived; the drawer is archived, whatever `IsActive`
            // says — an archived row that is also inactive must still be findable, or it could
            // never be restored.
            .Where(g => query.ArchivedOnly ? g.ArchivedAt != null : g.IsActive && g.ArchivedAt == null)
            .OrderBy(g => g.DefaultName)
            .ToListAsync(cancellationToken);

        // One aggregate for the page, not one per row.
        var usage = await GlobalIngredientUsage.CountByIngredientAsync(
            _context,
            ingredients.Select(g => g.Id).ToList(),
            cancellationToken);

        var dtos = ingredients
            .Select(ingredient => GlobalIngredientMapper.ToDto(
                ingredient,
                GlobalIngredientUsage.CountFor(usage, ingredient.Id)))
            .ToList();

        return ApiResponse<List<GlobalIngredientDto>>.SuccessWithData(dtos);
    }
}
