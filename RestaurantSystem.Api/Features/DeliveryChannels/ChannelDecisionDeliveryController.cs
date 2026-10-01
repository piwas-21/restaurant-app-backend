using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Features.DeliveryChannels.Commands.ClaimChannelDecisionCommand;
using RestaurantSystem.Api.Features.DeliveryChannels.Commands.ReportChannelDecisionCommand;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.DeliveryChannels;

[ApiController]
[Route("api/delivery-channels/decisions")]
[Authorize(Policy = DeliveryChannelServiceExtensions.MachineIngressPolicy)]
[ApiScope(ApiTokenScopes.ChannelOrdersWrite)]
public sealed class ChannelDecisionDeliveryController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("claim")]
    public async Task<ActionResult<ChannelDecisionLeaseDto?>> Claim()
        => Ok(await mediator.SendCommand(new ClaimChannelDecisionCommand(), HttpContext.RequestAborted));

    [HttpPost("{decisionId:guid}/report")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ChannelDecisionDto>> Report(Guid decisionId, ChannelDecisionReport report)
        => Ok(await mediator.SendCommand(new ReportChannelDecisionCommand(decisionId, report), HttpContext.RequestAborted));
}
