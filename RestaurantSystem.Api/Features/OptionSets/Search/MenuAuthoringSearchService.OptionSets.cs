using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

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
        var ranked = GetRankedOptionSets(normalizedQuery, searchText, forKind);
        if (cursor is not null)
        {
            ranked = AfterCursor(ranked, cursor);
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

    private static IQueryable<RankedOptionSet> AfterCursor(
        IQueryable<RankedOptionSet> sets,
        MenuAuthoringSearchCursor cursor) =>
        sets.Where(row => row.RelevanceRank > cursor.RelevanceRank
            || row.RelevanceRank == cursor.RelevanceRank
            && (MenuAuthoringCandidateTypes.OptionSetRank > cursor.TypeRank
                || MenuAuthoringCandidateTypes.OptionSetRank == cursor.TypeRank
                && (EF.Functions.Collate(row.OptionSet.Name, "C").CompareTo(cursor.Name) > 0
                    || EF.Functions.Collate(row.OptionSet.Name, "C").CompareTo(cursor.Name) == 0
                    && row.OptionSet.Id.CompareTo(cursor.Id) > 0)));

    private sealed class RankedOptionSet
    {
        public required OptionSet OptionSet { get; init; }
        public int RelevanceRank { get; init; }
    }
}
