using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed partial class MenuAuthoringSearchService
{
    public async Task<MenuAuthoringSearchPageDto> SearchAsync(
        string? query,
        OptionSetKind? forKind,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = ValidateQuery(query);
        var searchCursor = string.IsNullOrWhiteSpace(cursor) ? null : MenuAuthoringSearchCursor.Decode(cursor);
        var pageSize = Math.Clamp(limit, 1, 100);
        var decisions = await _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery)
            .Select(decision => new { decision.CandidateType, decision.CandidateId, decision.IsAccepted })
            .ToListAsync(cancellationToken);
        var accepted = decisions.Where(decision => decision.IsAccepted)
            .Select(decision => (decision.CandidateType, decision.CandidateId)).ToHashSet();
        var rejected = decisions.Where(decision => !decision.IsAccepted)
            .Select(decision => (decision.CandidateType, decision.CandidateId)).ToHashSet();
        var pattern = LikePattern(normalizedQuery);
        var candidates = new List<MenuAuthoringSearchCandidateDto>();

        if (forKind is null or OptionSetKind.BundleChoice or OptionSetKind.SuggestedSide)
        {
            candidates.AddRange(await SearchProductsAsync(pattern, forKind, searchCursor, accepted, rejected, pageSize, cancellationToken));
        }

        if (forKind is null or OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            candidates.AddRange(await SearchIngredientsAsync(pattern, forKind, searchCursor, accepted, rejected, pageSize, cancellationToken));
        }

        candidates.AddRange(await SearchOptionSetsAsync(pattern, forKind, searchCursor, accepted, rejected, pageSize, cancellationToken));

        foreach (var candidate in candidates)
        {
            var isNameMatch = OptionSetNameNormalizer.Normalize(candidate.Name).Contains(normalizedQuery, StringComparison.Ordinal);
            candidate.MatchSource = !isNameMatch && accepted.Contains((candidate.Type, candidate.Id)) ? "alias" : "name";
        }

        var ordered = candidates
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ThenBy(candidate => MenuAuthoringCandidateTypes.Rank(candidate.Type))
            .ThenBy(candidate => candidate.Id)
            .Take(pageSize + 1)
            .ToList();
        var hasMore = ordered.Count > pageSize;
        if (hasMore)
        {
            ordered.RemoveAt(ordered.Count - 1);
        }

        return new MenuAuthoringSearchPageDto
        {
            Items = ordered,
            NextCursor = hasMore && ordered.Count > 0 ? MenuAuthoringSearchCursor.Encode(ordered[^1]) : null
        };
    }

    private static string ValidateQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > 120)
        {
            throw new BadRequestException("Enter an authoring search term between 2 and 120 characters");
        }

        var normalized = OptionSetNameNormalizer.Normalize(query);
        if (normalized.Length is < 2 or > 160)
        {
            throw new BadRequestException("Enter an authoring search term between 2 and 120 characters");
        }

        return normalized;
    }
}
