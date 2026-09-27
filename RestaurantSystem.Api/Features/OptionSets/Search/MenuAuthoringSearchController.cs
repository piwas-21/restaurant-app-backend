using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OptionSets.Search;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.OptionSets;

[ApiController]
[Route("api/MenuAuthoring")]
public sealed class MenuAuthoringSearchController : ControllerBase
{
    private readonly IMenuAuthoringSearchService _search;
    private readonly MenuAuthoringPaginationSettings _pagination;

    public MenuAuthoringSearchController(
        IMenuAuthoringSearchService search,
        IOptions<MenuAuthoringPaginationSettings> pagination)
    {
        _search = search;
        _pagination = pagination.Value;
    }

    [HttpGet("search")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<MenuAuthoringSearchPageDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] OptionSetKind? forKind,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken = default)
    {
        var result = await _search.SearchAsync(q, forKind, cursor, _pagination.Normalize(limit), cancellationToken);
        return Ok(ApiResponse<MenuAuthoringSearchPageDto>.SuccessWithData(result));
    }

    [HttpPost("match-decisions")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<MenuAuthoringMatchDecisionDto>>> RecordDecision(
        [FromBody] MenuAuthoringMatchDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _search.RecordDecisionAsync(request, cancellationToken);
        return Ok(ApiResponse<MenuAuthoringMatchDecisionDto>.SuccessWithData(result));
    }
}
