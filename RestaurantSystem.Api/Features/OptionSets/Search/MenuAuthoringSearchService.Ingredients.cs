using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchIngredientsAsync(
        string pattern,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        HashSet<(string CandidateType, Guid CandidateId)> accepted,
        HashSet<(string CandidateType, Guid CandidateId)> rejected,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var acceptedIds = accepted.Where(item => item.CandidateType == MenuAuthoringCandidateTypes.Ingredient)
            .Select(item => item.CandidateId).ToList();
        var rejectedIds = rejected.Where(item => item.CandidateType == MenuAuthoringCandidateTypes.Ingredient)
            .Select(item => item.CandidateId).ToList();
        var ingredients = _context.GlobalIngredients.AsNoTracking()
            .Where(item => item.IsActive && item.ArchivedAt == null);
        if (forKind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            var kind = forKind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
            ingredients = ingredients.Where(item => item.Kind == kind);
        }

        ingredients = ingredients.Where(item =>
            EF.Functions.ILike(item.DefaultName, pattern, "\\")
            || item.Translations.Any(translation => EF.Functions.ILike(translation.Name, pattern, "\\"))
            || acceptedIds.Contains(item.Id));
        if (rejectedIds.Count > 0)
        {
            ingredients = ingredients.Where(item => !rejectedIds.Contains(item.Id));
        }

        if (cursor is not null)
        {
            ingredients = ingredients.Where(item =>
                EF.Functions.Collate(item.DefaultName, "C").CompareTo(cursor.Name) > 0
                || EF.Functions.Collate(item.DefaultName, "C").CompareTo(cursor.Name) == 0
                && (MenuAuthoringCandidateTypes.IngredientRank > cursor.TypeRank
                    || MenuAuthoringCandidateTypes.IngredientRank == cursor.TypeRank
                    && item.Id.CompareTo(cursor.Id) > 0));
        }

        return await ingredients
            .OrderBy(item => EF.Functions.Collate(item.DefaultName, "C"))
            .ThenBy(item => item.Id)
            .Take(pageSize + 1)
            .Select(item => new MenuAuthoringSearchCandidateDto
            {
                Id = item.Id,
                Type = MenuAuthoringCandidateTypes.Ingredient,
                Name = item.DefaultName,
                ImageUrl = item.ImageUrl,
                IngredientKind = item.Kind,
                IsActive = item.IsActive,
                IsAvailable = item.ArchivedAt == null
            })
            .ToListAsync(cancellationToken);
    }
}
