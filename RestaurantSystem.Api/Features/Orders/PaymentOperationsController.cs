using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetPaymentOperationQuery;

namespace RestaurantSystem.Api.Features.Orders;

/// <summary>Staff reconciliation surface for uncertain till payment writes.</summary>
[ApiController]
[Route("api/orders")]
public sealed class PaymentOperationsController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public PaymentOperationsController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Looks up by operation key only. Unknown is a successful result, not a payload-mismatch
    /// refusal or an operation-id oracle.
    /// </summary>
    [HttpGet("{orderId}/payments/operations/{operationId}")]
    [RequireStaff]
    public async Task<ActionResult<ApiResponse<PaymentOperationLookupDto>>> Get(
        Guid orderId, Guid operationId)
        => Ok(await _mediator.SendQuery(new GetPaymentOperationQuery(orderId, operationId)));
}
