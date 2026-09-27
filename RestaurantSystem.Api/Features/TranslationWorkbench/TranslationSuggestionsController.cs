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
public sealed class TranslationSuggestionsController(ITranslationSuggestionService suggestions) : ControllerBase
{
    [HttpPost("suggestions")]
    public async Task<ActionResult<ApiResponse<TranslationSuggestionsDto>>> Suggest(
        [FromBody] TranslationWorkbenchRequestDto request,
        CancellationToken cancellationToken) => Ok(ApiResponse<TranslationSuggestionsDto>.SuccessWithData(
        await suggestions.SuggestAsync(request, cancellationToken)));
}
