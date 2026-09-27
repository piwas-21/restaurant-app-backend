using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchIngredientsAsync(
        string normalizedQuery,
        string searchText,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var ranked = GetRankedIngredients(normalizedQuery, searchText, forKind);
        if (cursor is not null)
        {
            ranked = AfterCursor(ranked, cursor);
        }

        return await ranked
            .OrderBy(row => row.RelevanceRank)
            .ThenBy(row => EF.Functions.Collate(row.Ingredient.DefaultName, "C"))
            .ThenBy(row => row.Ingredient.Id)
            .Take(pageSize + 1)
            .Select(row => new MenuAuthoringSearchCandidateDto
            {
                Id = row.Ingredient.Id,
                Type = MenuAuthoringCandidateTypes.Ingredient,
                Name = row.Ingredient.DefaultName,
                RelevanceRank = row.RelevanceRank,
                ImageUrl = row.Ingredient.ImageUrl,
                IngredientKind = row.Ingredient.Kind,
                IsActive = row.Ingredient.IsActive,
                IsAvailable = row.Ingredient.ArchivedAt == null
            })
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<RankedIngredient> AfterCursor(
        IQueryable<RankedIngredient> ingredients,
        MenuAuthoringSearchCursor cursor) =>
        ingredients.Where(row => row.RelevanceRank > cursor.RelevanceRank
            || row.RelevanceRank == cursor.RelevanceRank
            && (MenuAuthoringCandidateTypes.IngredientRank > cursor.TypeRank
                || MenuAuthoringCandidateTypes.IngredientRank == cursor.TypeRank
                && (EF.Functions.Collate(row.Ingredient.DefaultName, "C").CompareTo(cursor.Name) > 0
                    || EF.Functions.Collate(row.Ingredient.DefaultName, "C").CompareTo(cursor.Name) == 0
                    && row.Ingredient.Id.CompareTo(cursor.Id) > 0)));

    private sealed class RankedIngredient
    {
        public required GlobalIngredient Ingredient { get; init; }
        public int RelevanceRank { get; init; }
    }
}
