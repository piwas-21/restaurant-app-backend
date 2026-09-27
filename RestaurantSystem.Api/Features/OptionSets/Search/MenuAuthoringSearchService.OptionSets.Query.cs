using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private IQueryable<RankedOptionSet> GetRankedOptionSets(
        string normalizedQuery,
        string searchText,
        OptionSetKind? forKind)
    {
        var decisions = _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery
                && decision.CandidateType == MenuAuthoringCandidateTypes.OptionSet);
        var acceptedIds = decisions.Where(decision => decision.IsAccepted).Select(decision => decision.CandidateId);
        var rejectedIds = decisions.Where(decision => !decision.IsAccepted).Select(decision => decision.CandidateId);
        var sets = ActiveOptionSets(forKind);
        var matches = MatchOptionSets(sets, acceptedIds, rejectedIds, searchText);
        return RankOptionSets(matches);
    }

    private IQueryable<OptionSet> ActiveOptionSets(OptionSetKind? forKind)
    {
        var sets = _context.OptionSets.AsNoTracking().Where(item => item.Status == OptionSetStatus.Active);
        if (forKind.HasValue)
        {
            sets = sets.Where(item => item.Kind == forKind.Value);
        }

        return sets;
    }

    private static IQueryable<OptionSetMatch> MatchOptionSets(
        IQueryable<OptionSet> sets,
        IQueryable<Guid> acceptedIds,
        IQueryable<Guid> rejectedIds,
        string searchText) => sets.Select(item => new OptionSetMatch
        {
            OptionSet = item,
            IsNameMatch = EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(item.Name),
                MenuAuthoringSearchDatabaseFunctions.Pattern(searchText), "\\"),
            IsTranslationMatch = item.Translations.Any(translation => EF.Functions.ILike(
                MenuAuthoringSearchDatabaseFunctions.Normalize(translation.Name),
                MenuAuthoringSearchDatabaseFunctions.Pattern(searchText), "\\")),
            IsExactMatch = MenuAuthoringSearchDatabaseFunctions.Normalize(item.Name)
                    == MenuAuthoringSearchDatabaseFunctions.Normalize(searchText)
                || item.Translations.Any(translation => MenuAuthoringSearchDatabaseFunctions.Normalize(translation.Name)
                    == MenuAuthoringSearchDatabaseFunctions.Normalize(searchText)),
            IsPrefixMatch = MenuAuthoringSearchDatabaseFunctions.Normalize(item.Name)
                    != string.Empty && EF.Functions.ILike(
                        MenuAuthoringSearchDatabaseFunctions.Normalize(item.Name),
                        MenuAuthoringSearchDatabaseFunctions.PrefixPattern(searchText), "\\")
                || item.Translations.Any(translation => EF.Functions.ILike(
                    MenuAuthoringSearchDatabaseFunctions.Normalize(translation.Name),
                    MenuAuthoringSearchDatabaseFunctions.PrefixPattern(searchText), "\\")),
            IsAliasMatch = acceptedIds.Contains(item.Id)
        })
            .Where(row => row.IsNameMatch || row.IsTranslationMatch || row.IsAliasMatch)
            .Where(row => !rejectedIds.Contains(row.OptionSet.Id));

    private static IQueryable<RankedOptionSet> RankOptionSets(IQueryable<OptionSetMatch> matches) =>
        matches.Select(row => new RankedOptionSet
        {
            OptionSet = row.OptionSet,
            RelevanceRank = row.IsExactMatch ? MenuAuthoringCandidateTypes.ExactMatchRank
                : row.IsPrefixMatch ? MenuAuthoringCandidateTypes.PrefixMatchRank
                : row.IsNameMatch || row.IsTranslationMatch ? MenuAuthoringCandidateTypes.NameMatchRank
                : MenuAuthoringCandidateTypes.AliasMatchRank
        });

    private sealed class OptionSetMatch
    {
        public required OptionSet OptionSet { get; init; }
        public bool IsNameMatch { get; init; }
        public bool IsTranslationMatch { get; init; }
        public bool IsExactMatch { get; init; }
        public bool IsPrefixMatch { get; init; }
        public bool IsAliasMatch { get; init; }
    }
}
