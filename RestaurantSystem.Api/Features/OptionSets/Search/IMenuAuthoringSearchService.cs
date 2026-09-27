using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public interface IMenuAuthoringSearchService
{
    Task<MenuAuthoringSearchPageDto> SearchAsync(
        string? query,
        OptionSetKind? forKind,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    Task<MenuAuthoringMatchDecisionDto> RecordDecisionAsync(
        MenuAuthoringMatchDecisionRequestDto request,
        CancellationToken cancellationToken);
}
