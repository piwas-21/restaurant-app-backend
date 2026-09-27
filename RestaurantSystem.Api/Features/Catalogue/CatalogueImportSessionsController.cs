using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.Api.Features.Catalogue;

[ApiController]
[Route("api/catalogue/import-sessions")]
[RequireAdmin]
public sealed class CatalogueImportSessionsController(
    ICatalogueImportSessionService sessions) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CatalogueImportSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CatalogueImportSessionDto>> Create(
        [FromBody] CreateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sessions.CreateAsync(request, cancellationToken));

    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(CatalogueImportSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogueImportSessionDto>> Get(Guid sessionId, CancellationToken cancellationToken) =>
        Ok(await sessions.GetAsync(sessionId, cancellationToken));

    [HttpPut("{sessionId:guid}/items")]
    [ProducesResponseType(typeof(CatalogueImportSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CatalogueImportSessionDto>> UpdateItems(
        Guid sessionId,
        [FromBody] UpdateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sessions.UpdateItemsAsync(sessionId, request, cancellationToken));

    [HttpPost("{sessionId:guid}/preview")]
    [ProducesResponseType(typeof(CatalogueImportPreviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CatalogueImportPreviewDto>> Preview(Guid sessionId, CancellationToken cancellationToken) =>
        Ok(await sessions.PreviewAsync(sessionId, cancellationToken));

    [HttpPost("{sessionId:guid}/import")]
    [ProducesResponseType(typeof(CatalogueImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CatalogueImportResultDto>> Import(
        Guid sessionId,
        [FromBody] ImportCatalogueSessionRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sessions.ImportAsync(sessionId, request, cancellationToken));

}
