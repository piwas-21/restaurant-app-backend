using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.CashierPrinting.Commands;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.CashierPrinting.Queries;

namespace RestaurantSystem.Api.Features.CashierPrinting;

[ApiController]
[RequireModule(ModuleIds.Printing)]
[Route("api/cashier-print-jobs")]
public sealed class CashierPrintingController(CustomMediator mediator) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin,Cashier,Server")]
    public async Task<ActionResult<ApiResponse<CashierPrintJobDto>>> Queue(
        QueueCashierPrintRequest request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<CashierPrintJobDto>.SuccessWithData(await mediator.SendCommand(
            new QueueCashierPrintCommand(request), cancellationToken)));

    [HttpGet("{jobId:guid}")]
    [Authorize(Roles = "Admin,Cashier,Server")]
    public async Task<ActionResult<ApiResponse<CashierPrintJobDto>>> Read(
        Guid jobId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<CashierPrintJobDto>.SuccessWithData(await mediator.SendQuery(
            new GetCashierPrintJobQuery(jobId), cancellationToken)));

    [HttpPost("claim")]
    [ApiKeyAuthFilter]
    public async Task<ActionResult<ApiResponse<CashierPrintJobDto?>>> Claim(
        [FromHeader(Name = "X-Device-Id")] string? deviceId, CancellationToken cancellationToken,
        [FromQuery] bool allowAutomatic = true) =>
        Ok(ApiResponse<CashierPrintJobDto?>.SuccessWithData(await mediator.SendCommand(
            new ClaimCashierPrintCommand(deviceId?.Trim() ?? string.Empty, allowAutomatic), cancellationToken)));

    [HttpPost("{jobId:guid}/complete")]
    [ApiKeyAuthFilter]
    public async Task<IActionResult> Complete(Guid jobId, CompleteCashierPrintRequest request,
        [FromHeader(Name = "X-Device-Id")] string? deviceId, CancellationToken cancellationToken)
    {
        await mediator.SendCommand(new CompleteCashierPrintCommand(jobId,
            deviceId?.Trim() ?? string.Empty, request.Status), cancellationToken);
        return Ok(ApiResponse<bool>.SuccessWithData(true));
    }
}
