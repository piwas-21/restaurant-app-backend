using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Features.DeliveryChannels.Commands.QueueChannelDecisionCommand;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelDecisionQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.DeliveryChannels;

[ApiController]
[Route("api/delivery-channels/orders/{orderId:guid}/decision")]
[RequireRole(UserRole.Admin, UserRole.Cashier)]
public sealed class ChannelOrderDecisionsController(CustomMediator mediator) : ControllerBase
{
    // No ApiScope annotation: machine identities cannot originate human staff decisions.
    [HttpPost]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ChannelDecisionDto>> Queue(Guid orderId, ChannelDecisionRequest request)
        => Accepted(await mediator.SendCommand(new QueueChannelDecisionCommand(orderId, request), HttpContext.RequestAborted));

    [HttpGet]
    public async Task<ActionResult<ChannelDecisionDto?>> Read(Guid orderId)
        => Ok(await mediator.SendQuery(new GetChannelDecisionQuery(orderId), HttpContext.RequestAborted));
}
