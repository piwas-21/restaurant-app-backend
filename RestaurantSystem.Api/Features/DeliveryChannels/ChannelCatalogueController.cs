using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Extensions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelAvailabilityQuery;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueQuery;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.DeliveryChannels;

[ApiController]
[Route("api/delivery-channels/catalogue")]
[Authorize(Policy = DeliveryChannelServiceExtensions.MachineCataloguePolicy)]
[ApiScope(ApiTokenScopes.ChannelCatalogueRead)]
public sealed class ChannelCatalogueController(CustomMediator mediator) : ControllerBase
{
    [HttpPost("availability")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ChannelAvailabilitySnapshot>> Availability(ChannelAvailabilityRequest request)
        => Ok(await mediator.SendQuery(new GetChannelAvailabilityQuery(request), HttpContext.RequestAborted));

    [HttpPost("snapshot")]
    [RequestSizeLimit(ExternalOrderLimits.RequestBytes)]
    public async Task<ActionResult<ChannelCatalogueSnapshot>> Snapshot(ChannelCatalogueRequest request)
        => Ok(await mediator.SendQuery(new GetChannelCatalogueQuery(request), HttpContext.RequestAborted));
}
