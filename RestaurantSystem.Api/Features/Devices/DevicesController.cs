using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Commands.RecordHeartbeatCommand;

namespace RestaurantSystem.Api.Features.Devices;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public DevicesController(CustomMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Device heartbeat — upserts the caller's fleet status (liveness, feed state, app version,
    /// non-secret config). Authenticated by the tenant's <c>X-Api-Key</c> (<see cref="ApiKeyAuthFilter"/>)
    /// plus the per-install <c>X-Device-Id</c> header, which the validator requires.
    /// </summary>
    [HttpPost("heartbeat")]
    [ApiKeyAuthFilter]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> Heartbeat(
        [FromBody] RecordHeartbeatCommand command,
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.SendCommand(
            command with { DeviceId = deviceId ?? string.Empty }, cancellationToken);
        return Ok(result);
    }
}
