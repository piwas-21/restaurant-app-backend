using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.Api.Features.Catalogue;

[ApiController]
[AllowAnonymous]
[Route("api/catalogue/templates")]
public sealed class CatalogueTemplatesController(ICatalogueTemplateQueryService templates) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPage(
        [FromQuery] string? type,
        [FromQuery] string? cuisine,
        [FromQuery(Name = "q")] string? query,
        [FromQuery] string? locale,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await templates.GetPageAsync(type, cuisine, query, locale, limit, cursor, cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }

    [HttpGet("{templateId}/revisions/{revision:int}")]
    public async Task<IActionResult> GetRevision(
        string templateId,
        int revision,
        CancellationToken cancellationToken)
    {
        var result = await templates.GetRevisionAsync(templateId, revision, cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }
}
