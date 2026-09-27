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
public sealed class TranslationWorkbenchController(
    ITranslationPreviewService preview,
    ITranslationSuggestionService suggestions,
    ITranslationReviewService review) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<ApiResponse<TranslationPreviewDto>>> Preview(
        [FromBody] TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken) => Ok(ApiResponse<TranslationPreviewDto>.SuccessWithData(
        await preview.PreviewAsync(request, cancellationToken)));

    [HttpPost("suggestions")]
    public async Task<ActionResult<ApiResponse<TranslationSuggestionsDto>>> Suggest(
        [FromBody] TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken) => Ok(ApiResponse<TranslationSuggestionsDto>.SuccessWithData(
        await suggestions.SuggestAsync(request, cancellationToken)));

    [HttpPost("review")]
    public async Task<ActionResult<ApiResponse<TranslationReviewDto>>> Review(
        [FromBody] TranslationReviewRequestDto request,
        CancellationToken cancellationToken) => Ok(ApiResponse<TranslationReviewDto>.SuccessWithData(
        await review.ReviewAsync(request, cancellationToken)));
}
