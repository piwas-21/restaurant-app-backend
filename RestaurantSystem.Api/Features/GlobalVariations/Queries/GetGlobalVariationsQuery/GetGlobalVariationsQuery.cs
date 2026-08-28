using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Api.Features.GlobalVariations.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Queries.GetGlobalVariationsQuery;

/// <param name="ArchivedOnly">The archive drawer rather than the shelf; see the ingredient twin.</param>
public record GetGlobalVariationsQuery(bool ArchivedOnly = false) : IQuery<ApiResponse<List<GlobalVariationDto>>>;

/// <summary>
/// The whole variation library in one response, ordered by name, with each row's usage count.
///
/// <para>
/// There is deliberately NO <c>/search</c> endpoint beside this one. The ingredient library has one
/// and S2 measured it as unusable: it short-circuits on a blank term, so it cannot browse, and it
/// matches <c>DefaultName</c> only, so it cannot help anyone who does not already know the English
/// word. The picker therefore reads the whole list and filters it in the browser across every
/// translation. This catalog is ~50 rows, an order of magnitude smaller than the 654 ingredients
/// that shape already serves, so a second endpoint would be a second thing to keep honest for no
/// gain. If it ever grows past a few thousand rows, both libraries need a paged server query — not
/// this one alone.
/// </para>
/// </summary>
public class GetGlobalVariationsQueryHandler : IQueryHandler<GetGlobalVariationsQuery, ApiResponse<List<GlobalVariationDto>>>
{
    private readonly ApplicationDbContext _context;

    public GetGlobalVariationsQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<List<GlobalVariationDto>>> Handle(GetGlobalVariationsQuery query, CancellationToken cancellationToken)
    {
        var variations = await _context.GlobalVariations
            .Include(g => g.Translations)
            // The shelf is active AND not archived; the drawer is archived, whatever `IsActive` says
            // — an archived row that is also inactive must still be findable, or it could never be
            // restored.
            .Where(g => query.ArchivedOnly ? g.ArchivedAt != null : g.IsActive && g.ArchivedAt == null)
            .OrderBy(g => g.DefaultName)
            .ToListAsync(cancellationToken);

        // One aggregate for the page, not one per row.
        var usage = await GlobalVariationUsage.CountByVariationAsync(
            _context,
            variations.Select(g => g.Id).ToList(),
            cancellationToken);

        var dtos = variations
            .Select(variation => GlobalVariationMapper.ToDto(
                variation,
                GlobalVariationUsage.CountFor(usage, variation.Id)))
            .ToList();

        return ApiResponse<List<GlobalVariationDto>>.SuccessWithData(dtos);
    }
}
