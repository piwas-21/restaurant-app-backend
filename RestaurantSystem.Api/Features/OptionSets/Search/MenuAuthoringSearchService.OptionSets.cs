using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    private async Task<List<MenuAuthoringSearchCandidateDto>> SearchOptionSetsAsync(
        string normalizedQuery,
        OptionSetKind? forKind,
        MenuAuthoringSearchCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var pattern = LikePattern(normalizedQuery);
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

        sets = sets.Where(item => EF.Functions.ILike(item.NormalizedName, pattern, "\\")
            || item.Translations.Any(translation => EF.Functions.ILike(translation.Name, pattern, "\\"))
            || acceptedIds.Contains(item.Id));
        sets = sets.Where(item => !rejectedIds.Contains(item.Id));

        if (cursor is not null)
        {
            sets = sets.Where(item =>
                EF.Functions.Collate(item.Name, "C").CompareTo(cursor.Name) > 0
                || EF.Functions.Collate(item.Name, "C").CompareTo(cursor.Name) == 0
                && (MenuAuthoringCandidateTypes.OptionSetRank > cursor.TypeRank
                    || MenuAuthoringCandidateTypes.OptionSetRank == cursor.TypeRank
                    && item.Id.CompareTo(cursor.Id) > 0));
        }

        return await sets
            .OrderBy(item => EF.Functions.Collate(item.Name, "C"))
            .ThenBy(item => item.Id)
            .Take(pageSize + 1)
            .Select(item => new MenuAuthoringSearchCandidateDto
            {
                Id = item.Id,
                Type = MenuAuthoringCandidateTypes.OptionSet,
                Name = item.Name,
                Version = item.Version,
                EntryCount = item.Entries.Count(entry => entry.IsEnabled),
                AttachmentCount = item.Attachments.Count,
                IsActive = item.Status == OptionSetStatus.Active,
                IsAvailable = item.Status == OptionSetStatus.Active
            })
            .ToListAsync(cancellationToken);
    }
}
