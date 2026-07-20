using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Commands.RecordDeviceEventsCommand;
using RestaurantSystem.Api.Features.Devices.Commands.RecordHeartbeatCommand;
using RestaurantSystem.Api.Features.Devices.Commands.RecordPrintAcksCommand;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Devices.Queries.GetDeviceEventsQuery;
using RestaurantSystem.Api.Features.Devices.Queries.GetDevicesQuery;
using RestaurantSystem.Api.Features.Devices.Queries.GetMissedOrdersQuery;

namespace RestaurantSystem.Api.Features.Devices;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    // API defaults (operator-tunable via query string), named so they aren't magic literals.
    private const int DefaultEventLimit = 100;
    private const int DefaultGraceMinutes = 15;
    private const int DefaultLookbackHours = 24;

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

    /// <summary>
    /// Batched order print acknowledgements — upserts by <c>(OrderId, DeviceId, Target)</c> so an
    /// at-least-once outbox is idempotent. Feeds the backend's served-vs-acked missed-order detector.
    /// </summary>
    [HttpPost("print-acks")]
    [ApiKeyAuthFilter]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> PrintAcks(
        [FromBody] RecordPrintAcksCommand command,
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.SendCommand(
            command with { DeviceId = deviceId ?? string.Empty }, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Batched diagnostic events (errors/warnings/health) — de-duplicated by
    /// <c>(DeviceId, ClientEventId)</c> so a retrying outbox never double-inserts.
    /// </summary>
    [HttpPost("events")]
    [ApiKeyAuthFilter]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> Events(
        [FromBody] RecordDeviceEventsCommand command,
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.SendCommand(
            command with { DeviceId = deviceId ?? string.Empty }, cancellationToken);
        return Ok(result);
    }

    /// <summary>Admin: list every known device with its last-reported fleet status (most-recent first).</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<List<DeviceSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<DeviceSummaryDto>>>> GetDevices(
        CancellationToken cancellationToken)
        => Ok(await _mediator.SendQuery(new GetDevicesQuery(), cancellationToken));

    /// <summary>Admin: recent diagnostic events for one device, newest-first.</summary>
    [HttpGet("{deviceId}/events")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<List<DeviceEventLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<DeviceEventLogDto>>>> GetDeviceEvents(
        string deviceId,
        [FromQuery] int limit = DefaultEventLimit,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.SendQuery(new GetDeviceEventsQuery(deviceId, limit), cancellationToken));

    /// <summary>Admin: recent confirmed orders (within <c>lookbackHours</c>) past the grace window
    /// with no Printed receipt — i.e. served to the feed but never printed (missed orders).</summary>
    [HttpGet("missed-orders")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<List<MissedOrderDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<List<MissedOrderDto>>>> GetMissedOrders(
        [FromQuery] int graceMinutes = DefaultGraceMinutes,
        [FromQuery] int lookbackHours = DefaultLookbackHours,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.SendQuery(
            new GetMissedOrdersQuery(graceMinutes, lookbackHours), cancellationToken));
}
