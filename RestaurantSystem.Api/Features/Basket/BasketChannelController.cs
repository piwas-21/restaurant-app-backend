using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Commands.SetBasketOrderTypeCommand;
using RestaurantSystem.Api.Features.Basket.Dtos;

namespace RestaurantSystem.Api.Features.Basket;

/// <summary>
/// The basket's CHANNEL, split out of <c>BasketController</c> (ORDER-TYPE-AVAILABILITY-PLAN §9.7).
/// </summary>
/// <remarks>
/// The basket's other write endpoints mutate LINES; this one mutates which order type the basket is
/// ordered through, and is the only one that can answer 200 while REFUSING to do what was asked (the
/// two-phase conflict protocol below). That is the seam.
/// <para>
/// It is not, however, why the file was over its limit — worth stating, because "the order-type work
/// bloated the controller" is the plausible story and it is false: the controller was already
/// 181/150 and baselined before any of this feature landed, and the order-type action added the 22
/// lines that took it to 203. §9.13's upsert and §9.15's conflict scan both landed in
/// <c>BasketChannelService</c> and never touched a controller.
/// </para>
/// <para>
/// The route prefix comes from <see cref="BasketControllerBase"/>, so the URL stays
/// <c>/api/Basket/order-type</c> — that is contract, and <c>BasketRoutingContractTests</c> pins it
/// because a controller split gets exactly this wrong silently. Two controllers sharing one prefix
/// is fine; an action collision between them would be an <c>AmbiguousMatchException</c> at request
/// time rather than at startup, so keep the action sets disjoint.
/// </para>
/// </remarks>
public class BasketChannelController : BasketControllerBase
{
    public BasketChannelController(CustomMediator mediator) : base(mediator)
    {
    }

    /// <summary>
    /// Set the basket's order type (channel). Call with removeConflicts=false first: if any line is
    /// unavailable for the new type NOTHING changes and the conflicts come back, so the guest can
    /// confirm. Repeat with removeConflicts=true to drop those lines and apply the switch.
    /// </summary>
    [HttpPut("order-type")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BasketChannelSwitchDto>>> SetOrderType(
        [FromHeader(Name = SessionIdHeader)] string sessionId,
        [FromBody] SetBasketOrderTypeCommand command)
    {
        if (MissingSession<BasketChannelSwitchDto>(sessionId) is { } error) return error;

        command.SessionId = sessionId;
        return Ok(await Mediator.SendCommand(command));
    }
}
