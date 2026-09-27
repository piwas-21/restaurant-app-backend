using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.OptionSets;

[ApiController]
[Route("api/OptionSets/{id:guid}")]
public sealed class OptionSetMaterializationController : ControllerBase
{
    private readonly IOptionSetMaterializer _materializer;

    public OptionSetMaterializationController(IOptionSetMaterializer materializer)
    {
        _materializer = materializer;
    }

    [HttpPost("preview")]
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

    [HttpPost("apply")]
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
