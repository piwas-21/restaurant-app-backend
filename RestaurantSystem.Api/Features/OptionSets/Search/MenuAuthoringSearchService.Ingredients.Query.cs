using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private IQueryable<RankedIngredient> GetRankedIngredients(
        string normalizedQuery,
        string searchText,
        OptionSetKind? forKind)
    {
        var decisions = _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery
                && decision.CandidateType == MenuAuthoringCandidateTypes.Ingredient);
        var acceptedIds = decisions.Where(decision => decision.IsAccepted).Select(decision => decision.CandidateId);
        var rejectedIds = decisions.Where(decision => !decision.IsAccepted).Select(decision => decision.CandidateId);
        var ingredients = IngredientCandidates(forKind);
        var matches = MatchIngredients(ingredients, acceptedIds, rejectedIds, searchText);
        return RankIngredients(matches);
    }

    private IQueryable<GlobalIngredient> IngredientCandidates(OptionSetKind? forKind)
    {
        var ingredients = _context.GlobalIngredients.AsNoTracking()
            .Where(item => item.IsActive && item.ArchivedAt == null);
        if (forKind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            var kind = forKind == OptionSetKind.Sauce ? IngredientKind.Sauce : IngredientKind.Ingredient;
            ingredients = ingredients.Where(item => item.Kind == kind);
        }

        return ingredients;
    }

    private static IQueryable<IngredientMatch> MatchIngredients(
        IQueryable<GlobalIngredient> ingredients,
        IQueryable<Guid> acceptedIds,
        IQueryable<Guid> rejectedIds,
        string searchText) => ingredients.Select(item => new IngredientMatch
        {
            Ingredient = item,
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

    private static IQueryable<RankedIngredient> RankIngredients(IQueryable<IngredientMatch> matches) =>
        matches.Select(row => new RankedIngredient
        {
            Ingredient = row.Ingredient,
            RelevanceRank = row.IsExactMatch ? MenuAuthoringCandidateTypes.ExactMatchRank
                : row.IsPrefixMatch ? MenuAuthoringCandidateTypes.PrefixMatchRank
                : row.IsDefaultNameMatch || row.IsTranslationMatch ? MenuAuthoringCandidateTypes.NameMatchRank
                : MenuAuthoringCandidateTypes.AliasMatchRank
        });

    private sealed class IngredientMatch
    {
        public required GlobalIngredient Ingredient { get; init; }
        public bool IsDefaultNameMatch { get; init; }
        public bool IsTranslationMatch { get; init; }
        public bool IsExactMatch { get; init; }
        public bool IsPrefixMatch { get; init; }
        public bool IsAliasMatch { get; init; }
    }
}
