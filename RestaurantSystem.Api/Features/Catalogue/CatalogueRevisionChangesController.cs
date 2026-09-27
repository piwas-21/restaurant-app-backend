using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.Api.Features.Catalogue;

[ApiController]
[Route("api/catalogue/import-sessions/{sessionId:guid}/revision-changes")]
[RequireAdmin]
public sealed class CatalogueRevisionChangesController(
    ICatalogueRevisionChangeService revisionChanges) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CatalogueRevisionChangesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CatalogueRevisionChangesDto>> Get(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        Ok(await revisionChanges.GetAsync(sessionId, cancellationToken));

    [HttpPost("apply")]
    [ProducesResponseType(typeof(CatalogueRevisionFieldApplyResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CatalogueRevisionFieldApplyResultDto>> Apply(
        Guid sessionId,
        [FromBody] ApplyCatalogueRevisionFieldsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await revisionChanges.ApplyFieldsAsync(sessionId, request, cancellationToken));
}
