using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Enums;

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
        var searchText = query!.Trim();
        var searchCursor = string.IsNullOrWhiteSpace(cursor)
            ? null
            : MenuAuthoringSearchCursor.Decode(cursor, _pagination.MaximumSearchCursorLength);
        var pageSize = PageSize(limit);
        var candidates = new List<MenuAuthoringSearchCandidateDto>();

        if (forKind is null or OptionSetKind.BundleChoice or OptionSetKind.SuggestedSide)
        {
            candidates.AddRange(await SearchProductsAsync(
                normalizedQuery, searchText, forKind, searchCursor, pageSize, cancellationToken));
        }

        if (forKind is null or OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            candidates.AddRange(await SearchIngredientsAsync(
                normalizedQuery, searchText, forKind, searchCursor, pageSize, cancellationToken));
        }

        candidates.AddRange(await SearchOptionSetsAsync(
            normalizedQuery, searchText, forKind, searchCursor, pageSize, cancellationToken));
        var accepted = await LoadAcceptedDecisionsAsync(normalizedQuery, candidates, cancellationToken);

        foreach (var candidate in candidates)
        {
            var isNameMatch = OptionSetNameNormalizer.Normalize(candidate.Name).Contains(normalizedQuery, StringComparison.Ordinal);
            candidate.MatchSource = !isNameMatch && accepted.Contains((candidate.Type, candidate.Id)) ? "alias" : "name";
        }

        var ordered = candidates
            .OrderBy(candidate => candidate.RelevanceRank)
            .ThenBy(candidate => MenuAuthoringCandidateTypes.Rank(candidate.Type))
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
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

    private async Task<HashSet<(string CandidateType, Guid CandidateId)>> LoadAcceptedDecisionsAsync(
        string normalizedQuery,
        List<MenuAuthoringSearchCandidateDto> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var candidateIds = candidates.Select(candidate => candidate.Id).Distinct().ToList();
        var candidateTypes = candidates.Select(candidate => candidate.Type).Distinct().ToList();
        // Search sources page-bound candidates; the unique decision key bounds this result to their ID/type pairs.
        var maximumDecisions = candidateIds.Count * candidateTypes.Count;
        var decisions = await _context.OptionSetMatchDecisions.AsNoTracking()
            .Where(decision => decision.NormalizedName == normalizedQuery && decision.IsAccepted
                && candidateIds.Contains(decision.CandidateId) && candidateTypes.Contains(decision.CandidateType))
            .Take(maximumDecisions)
            .Select(decision => new { decision.CandidateType, decision.CandidateId })
            .ToListAsync(cancellationToken);

        return decisions.Select(decision => (decision.CandidateType, decision.CandidateId)).ToHashSet();
    }

    private string ValidateQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length > _pagination.MaximumSearchQueryLength)
        {
            throw new BadRequestException(
                $"Enter an authoring search term with at most {_pagination.MaximumSearchQueryLength} characters");
        }

        var normalized = OptionSetNameNormalizer.Normalize(query);
        if (normalized.Length < _pagination.MinimumNormalizedSearchQueryLength
            || normalized.Length > _pagination.MaximumNormalizedSearchQueryLength)
        {
            throw new BadRequestException(
                $"Enter a normalized authoring search term between {_pagination.MinimumNormalizedSearchQueryLength} and {_pagination.MaximumNormalizedSearchQueryLength} characters");
        }

        return normalized;
    }
}
