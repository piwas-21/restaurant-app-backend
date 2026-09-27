using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchOptionSetsAsync(
        string normalizedQuery,
        string searchText,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var decisions = _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery
                && decision.CandidateType == MenuAuthoringCandidateTypes.OptionSet);
        var acceptedIds = decisions.Where(decision => decision.IsAccepted).Select(decision => decision.CandidateId);
        var rejectedIds = decisions.Where(decision => !decision.IsAccepted).Select(decision => decision.CandidateId);
        var sets = _context.OptionSets.AsNoTracking().Where(item => item.Status == OptionSetStatus.Active);
        if (forKind.HasValue)
        {
            sets = sets.Where(item => item.Kind == forKind.Value);
        }

        var matching = sets.Select(item => new
        {
            OptionSet = item,
            SearchName = MenuAuthoringSearchDatabaseFunctions.Normalize(item.Name),
            SearchTerm = MenuAuthoringSearchDatabaseFunctions.Normalize(searchText),
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
        var ranked = matching.Select(row => new RankedOptionSet
        {
            OptionSet = row.OptionSet,
            RelevanceRank = row.IsExactMatch ? MenuAuthoringCandidateTypes.ExactMatchRank
                : row.IsPrefixMatch ? MenuAuthoringCandidateTypes.PrefixMatchRank
                : row.IsNameMatch || row.IsTranslationMatch ? MenuAuthoringCandidateTypes.NameMatchRank
                : MenuAuthoringCandidateTypes.AliasMatchRank
        });

        if (cursor is not null)
        {
            ranked = ranked.Where(row => row.RelevanceRank > cursor.RelevanceRank
                || row.RelevanceRank == cursor.RelevanceRank
                && (MenuAuthoringCandidateTypes.OptionSetRank > cursor.TypeRank
                    || MenuAuthoringCandidateTypes.OptionSetRank == cursor.TypeRank
                    && (EF.Functions.Collate(row.OptionSet.Name, "C").CompareTo(cursor.Name) > 0
                        || EF.Functions.Collate(row.OptionSet.Name, "C").CompareTo(cursor.Name) == 0
                        && row.OptionSet.Id.CompareTo(cursor.Id) > 0)));
        }

        return await ranked
            .OrderBy(row => row.RelevanceRank)
            .ThenBy(row => EF.Functions.Collate(row.OptionSet.Name, "C"))
            .ThenBy(row => row.OptionSet.Id)
            .Take(pageSize + 1)
            .Select(row => new MenuAuthoringSearchCandidateDto
            {
                Id = row.OptionSet.Id,
                Type = MenuAuthoringCandidateTypes.OptionSet,
                Name = row.OptionSet.Name,
                RelevanceRank = row.RelevanceRank,
                Version = row.OptionSet.Version,
                EntryCount = row.OptionSet.Entries.Count(entry => entry.IsEnabled),
                AttachmentCount = row.OptionSet.Attachments.Count,
                IsActive = row.OptionSet.Status == OptionSetStatus.Active,
                IsAvailable = row.OptionSet.Status == OptionSetStatus.Active
            })
            .ToListAsync(cancellationToken);
    }

    private sealed class RankedOptionSet
    {
        public required OptionSet OptionSet { get; init; }
        public int RelevanceRank { get; init; }
    }
}
