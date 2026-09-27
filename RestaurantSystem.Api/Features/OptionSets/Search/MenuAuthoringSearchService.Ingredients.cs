using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

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
        var decisions = _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery
                && decision.CandidateType == MenuAuthoringCandidateTypes.Ingredient);
        var acceptedIds = decisions.Where(decision => decision.IsAccepted).Select(decision => decision.CandidateId);
        var rejectedIds = decisions.Where(decision => !decision.IsAccepted).Select(decision => decision.CandidateId);
        var ingredients = _context.GlobalIngredients.AsNoTracking()
            .Where(item => item.IsActive && item.ArchivedAt == null);
        if (forKind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            var kind = forKind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
            ingredients = ingredients.Where(item => item.Kind == kind);
        }

        var matching = ingredients.Select(item => new
        {
            Ingredient = item,
            SearchName = MenuAuthoringSearchDatabaseFunctions.Normalize(item.DefaultName),
            SearchTerm = MenuAuthoringSearchDatabaseFunctions.Normalize(searchText),
            IsDefaultNameMatch = EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(item.DefaultName),
                MenuAuthoringSearchDatabaseFunctions.Pattern(searchText), "\\"),
            IsTranslationMatch = item.Translations.Any(translation => EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(translation.Name),
                MenuAuthoringSearchDatabaseFunctions.Pattern(searchText), "\\")),
            IsExactMatch = MenuAuthoringSearchDatabaseFunctions.Normalize(item.DefaultName)
                    == MenuAuthoringSearchDatabaseFunctions.Normalize(searchText)
                || item.Translations.Any(translation => MenuAuthoringSearchDatabaseFunctions.Normalize(translation.Name)
                    == MenuAuthoringSearchDatabaseFunctions.Normalize(searchText)),
            IsPrefixMatch = EF.Functions.ILike(
                    MenuAuthoringSearchDatabaseFunctions.Normalize(item.DefaultName),
                    MenuAuthoringSearchDatabaseFunctions.PrefixPattern(searchText), "\\")
                || item.Translations.Any(translation => EF.Functions.ILike(
                    MenuAuthoringSearchDatabaseFunctions.Normalize(translation.Name),
                    MenuAuthoringSearchDatabaseFunctions.PrefixPattern(searchText), "\\")),
            IsAliasMatch = acceptedIds.Contains(item.Id)
        })
            .Where(row => row.IsDefaultNameMatch || row.IsTranslationMatch || row.IsAliasMatch)
            .Where(row => !rejectedIds.Contains(row.Ingredient.Id));
        var ranked = matching.Select(row => new RankedIngredient
        {
            Ingredient = row.Ingredient,
            RelevanceRank = row.IsExactMatch ? MenuAuthoringCandidateTypes.ExactMatchRank
                : row.IsPrefixMatch ? MenuAuthoringCandidateTypes.PrefixMatchRank
                : row.IsDefaultNameMatch || row.IsTranslationMatch ? MenuAuthoringCandidateTypes.NameMatchRank
                : MenuAuthoringCandidateTypes.AliasMatchRank
        });

        if (cursor is not null)
        {
            ranked = ranked.Where(row => row.RelevanceRank > cursor.RelevanceRank
                || row.RelevanceRank == cursor.RelevanceRank
                && (MenuAuthoringCandidateTypes.IngredientRank > cursor.TypeRank
                    || MenuAuthoringCandidateTypes.IngredientRank == cursor.TypeRank
                    && (EF.Functions.Collate(row.Ingredient.DefaultName, "C").CompareTo(cursor.Name) > 0
                        || EF.Functions.Collate(row.Ingredient.DefaultName, "C").CompareTo(cursor.Name) == 0
                        && row.Ingredient.Id.CompareTo(cursor.Id) > 0)));
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

    private sealed class RankedIngredient
    {
        public required GlobalIngredient Ingredient { get; init; }
        public int RelevanceRank { get; init; }
    }
}
