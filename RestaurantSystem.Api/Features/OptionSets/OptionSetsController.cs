using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.OptionSets;

[ApiController]
[Route("api/[controller]")]
public sealed class OptionSetsController : ControllerBase
{
    private readonly IOptionSetCatalogService _catalog;
    private readonly IOptionSetMaterializer _materializer;
    private readonly MenuAuthoringPaginationSettings _pagination;

    public OptionSetsController(
        IOptionSetCatalogService catalog,
        IOptionSetMaterializer materializer,
        IOptions<MenuAuthoringPaginationSettings> pagination)
    {
        _catalog = catalog;
        _materializer = materializer;
        _pagination = pagination.Value;
    }

    [HttpGet]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetPageDto>>> Search(
        [FromQuery] OptionSetKind? kind,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken = default)
    {
        var result = await _catalog.SearchAsync(kind, q, cursor, _pagination.Normalize(limit), cancellationToken);
        return Ok(ApiResponse<OptionSetPageDto>.SuccessWithData(result));
    }

    [HttpPost]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetDetailDto>>> Create(
        [FromBody] OptionSetWriteRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.CreateAsync(request, cancellationToken);
        Response.Headers.ETag = MenuAuthoringVersionTag.Format(result.Version);
        return Ok(ApiResponse<OptionSetDetailDto>.SuccessWithData(result));
    }

    [HttpGet("{id:guid}")]
    [ApiScope(ApiTokenScopes.MenuRead)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetDetailDto>>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.GetAsync(id, cancellationToken);
        Response.Headers.ETag = MenuAuthoringVersionTag.Format(result.Version);
        return Ok(ApiResponse<OptionSetDetailDto>.SuccessWithData(result));
    }

    [HttpPut("{id:guid}")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetDetailDto>>> Update(
        Guid id,
        [FromBody] OptionSetWriteRequestDto request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!MenuAuthoringVersionTag.TryParse(ifMatch, out var expectedVersion))
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiResponse<OptionSetDetailDto>.Failure("Send the current option-set ETag in If-Match before saving"));
        }

        var result = await _catalog.UpdateAsync(id, expectedVersion, request, cancellationToken);
        Response.Headers.ETag = MenuAuthoringVersionTag.Format(result.Version);
        return Ok(ApiResponse<OptionSetDetailDto>.SuccessWithData(result));
    }

    [HttpPost("{id:guid}/preview")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetMaterializationPreview>>> Preview(
        Guid id,
        [FromBody] OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        request.OptionSetId = id;
        var result = await _materializer.PreviewAsync(request, cancellationToken);
        return Ok(ApiResponse<OptionSetMaterializationPreview>.SuccessWithData(result));
    }

    [HttpPost("{id:guid}/apply")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<OptionSetMaterializationResult>>> Apply(
        Guid id,
        [FromBody] OptionSetMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        request.OptionSetId = id;
        var result = await _materializer.ApplyAsync(request, cancellationToken);
        return Ok(ApiResponse<OptionSetMaterializationResult>.SuccessWithData(result));
    }
}
