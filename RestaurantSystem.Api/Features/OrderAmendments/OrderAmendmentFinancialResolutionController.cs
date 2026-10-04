using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Features.OrderAmendments;

/// <summary>Admin-only review, settlement, and recovery for paid amendment credits.</summary>
[ApiController]
[Authorize]
[RequireAdmin]
[Route("api/staff/orders/{orderId:guid}/amendments/{amendmentId:guid}/financial-resolution")]
public sealed class OrderAmendmentFinancialResolutionController(
    IOrderAmendmentResolutionService service) : ControllerBase
{
    [HttpGet("context")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionContextDto>>> Context(
        Guid orderId, Guid amendmentId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<OrderAmendmentResolutionContextDto>.SuccessWithData(
            await service.ContextAsync(orderId, amendmentId, cancellationToken)));

    [HttpGet("recovery")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionRecoveryDto>>> RecoverForAmendment(
        Guid orderId, Guid amendmentId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<OrderAmendmentResolutionRecoveryDto>.SuccessWithData(
            await service.RecoverForAmendmentAsync(orderId, amendmentId, cancellationToken)));

    [HttpPost("quote")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionQuoteDto>>> Quote(
        Guid orderId, Guid amendmentId, [FromBody] OrderAmendmentResolutionQuoteRequest request,
        CancellationToken cancellationToken) => Ok(ApiResponse<OrderAmendmentResolutionQuoteDto>.SuccessWithData(
        await service.QuoteAsync(orderId, amendmentId, request, cancellationToken)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>> Start(
        Guid orderId, Guid amendmentId, [FromBody] OrderAmendmentResolutionStartRequest request,
        CancellationToken cancellationToken) => Ok(ApiResponse<OrderAmendmentResolutionStartOutcomeDto>.SuccessWithData(
        await service.StartAsync(orderId, amendmentId, request, cancellationToken)));

    [HttpGet("operations/{clientOperationId:guid}")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>> LookupByClientKey(
        Guid orderId, Guid amendmentId, Guid clientOperationId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<OrderAmendmentResolutionStartOutcomeDto>.SuccessWithData(
            await service.LookupByClientKeyAsync(orderId, amendmentId, clientOperationId, cancellationToken)));

    [HttpGet("~/api/staff/amendment-financial-resolution-operations/{operationId:guid}")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionResultDto>>> Lookup(
        Guid operationId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<OrderAmendmentResolutionResultDto>.SuccessWithData(
            await service.LookupAsync(operationId, cancellationToken)));

    [HttpPost("~/api/staff/amendment-financial-resolution-operations/{operationId:guid}/recover")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionResultDto>>> Recover(
        Guid operationId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<OrderAmendmentResolutionResultDto>.SuccessWithData(
            await service.RecoverAsync(operationId, cancellationToken)));

    [HttpPost("~/api/staff/amendment-financial-resolution-operations/{operationId:guid}/confirm-till")]
    public async Task<ActionResult<ApiResponse<OrderAmendmentResolutionResultDto>>> ConfirmTill(
        Guid operationId, [FromBody] ManualTillConfirmationRequest request,
        CancellationToken cancellationToken) =>
        Ok(ApiResponse<OrderAmendmentResolutionResultDto>.SuccessWithData(
            await service.ConfirmTillAsync(operationId, request, cancellationToken)));

    [HttpGet("~/api/staff/orders/{orderId:guid}/amendment-financial-resolution-recovery")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<OrderAmendmentResolutionRecoveryDto>>>>
        ListRecoverableForOrder(Guid orderId, CancellationToken cancellationToken) =>
        Ok(ApiResponse<IReadOnlyList<OrderAmendmentResolutionRecoveryDto>>.SuccessWithData(
            await service.ListRecoverableForOrderAsync(orderId, cancellationToken)));
}
