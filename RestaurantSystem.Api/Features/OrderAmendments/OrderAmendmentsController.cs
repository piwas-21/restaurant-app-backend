using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.OrderAmendments.Commands;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Queries;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Features.OrderAmendments;

/// <summary>Staff-only quote, commit, replay and history surface for native order amendments.</summary>
[ApiController]
[Route("api/staff/orders/{orderId:guid}/amendments")]
[Authorize]
[RequireTableServiceStaff]
[RequireModule(ModuleIds.Server, ModuleIds.Cashier)]
public sealed class OrderAmendmentsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public OrderAmendmentsController(CustomMediator mediator) => _mediator = mediator;

    [HttpPost("quote")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentQuoteDto>>> Quote(
        Guid orderId, [FromBody] OrderAmendmentQuoteRequest request) =>
        Ok(await _mediator.SendCommand(new QuoteOrderAmendmentCommand(orderId, request)));

    [HttpPost("commit")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentCommitDto>>> Commit(
        Guid orderId, [FromBody] OrderAmendmentCommitRequest request) =>
        Ok(await _mediator.SendCommand(new CommitOrderAmendmentCommand(orderId, request)));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderAmendmentHistoryDto>>>> History(
        Guid orderId) =>
        Ok(await _mediator.SendQuery(new GetOrderAmendmentsQuery(orderId)));

    [HttpGet("~/api/staff/amendment-operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentOperationLookupDto>>> Operation(
        Guid operationId) =>
        Ok(await _mediator.SendQuery(new GetOrderAmendmentOperationQuery(operationId)));
}
