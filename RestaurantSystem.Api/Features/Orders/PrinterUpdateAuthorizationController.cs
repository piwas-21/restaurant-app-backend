using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetPrinterUpdateAuthorizationQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders;

[ApiController]
[RequireModule(ModuleIds.Printing)]
[Route("api/printer-feed/updates")]
public sealed class PrinterUpdateAuthorizationController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("{jobId:guid}/authorization")]
    [ApiKeyAuthFilter]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<PrinterUpdateAuthorizationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PrinterUpdateAuthorizationDto>>> GetAuthorization(
        Guid jobId, [FromQuery] int revision, [FromQuery] DevicePrintTarget target,
        CancellationToken cancellationToken)
        => Ok(ApiResponse<PrinterUpdateAuthorizationDto>.SuccessWithData(await mediator.SendQuery(
            new GetPrinterUpdateAuthorizationQuery(jobId, revision, target), cancellationToken)));
}
