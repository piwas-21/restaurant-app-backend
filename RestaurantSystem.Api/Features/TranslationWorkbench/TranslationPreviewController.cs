using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.TranslationWorkbench;

[ApiController]
[Route("api/translation-workbench")]
[ApiScope(ApiTokenScopes.MenuWrite)]
[RequireAdmin]
public sealed class TranslationPreviewController(ITranslationPreviewService preview) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<ApiResponse<TranslationPreviewDto>>> Preview(
        [FromBody] TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken) => Ok(ApiResponse<TranslationPreviewDto>.SuccessWithData(
        await preview.PreviewAsync(request, cancellationToken)));
}
