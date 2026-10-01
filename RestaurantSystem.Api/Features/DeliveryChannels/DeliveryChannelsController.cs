using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Features.DeliveryChannels.Commands.CreateExternalOrderCommand;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.DeliveryChannels;

[ApiController]
[Route("api/delivery-channels")]
[Authorize(Policy = DeliveryChannelServiceExtensions.MachineIngressPolicy)]
[ApiScope(ApiTokenScopes.ChannelOrdersWrite)]
public sealed class DeliveryChannelsController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("orders")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ExternalOrderImportDto>> Import(ExternalOrderRequest request)
        => Ok(await mediator.SendCommand(new CreateExternalOrderCommand(request), HttpContext.RequestAborted));
}
