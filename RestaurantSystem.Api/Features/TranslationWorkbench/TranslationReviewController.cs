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
public sealed class TranslationReviewController(ITranslationReviewService review) : ControllerBase
{
    [HttpPost("review")]
    public async Task<ActionResult<ApiResponse<TranslationReviewDto>>> Review(
        [FromBody] TranslationReviewRequestDto request,
        CancellationToken cancellationToken) => Ok(ApiResponse<TranslationReviewDto>.SuccessWithData(
        await review.ReviewAsync(request, cancellationToken)));
}
