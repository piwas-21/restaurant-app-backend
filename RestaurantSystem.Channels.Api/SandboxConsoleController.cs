using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace RestaurantSystem.Channels.Api;

[ApiController]
[Route("api/sandbox/uber")]
[EnableRateLimiting("sandbox-console")]
[RequestSizeLimit(4096)]
public sealed class SandboxConsoleController(ISandboxSessions sessions, ISandboxConnection connection,
    ChannelMediator mediator) : ControllerBase
{
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string state = "", [FromQuery] string code = "",
        [FromQuery] string error = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await sessions.Require(HttpContext, false, cancellationToken);
            await connection.Complete(session, state, code, error, cancellationToken);
            return Redirect("/console/?connection=complete");
        }
        catch (ChannelConsoleException)
        {
            // Never put callback credentials or provider responses in a redirect or log.
            return Redirect("/console/?connection=failed");
        }
    }

    [HttpGet("{operation:regex(^(configuration|preview|menu|verification|receipts|availability|imports)$)}")]
    public async Task<IActionResult> Query(string operation, [FromQuery] string cursor = "", CancellationToken cancellationToken = default)
    {
        await sessions.Require(HttpContext, false, cancellationToken);
        return Ok(await mediator.SendQuery<ConsoleQuery, System.Text.Json.JsonElement>(new(operation, Cursor: cursor), cancellationToken));
    }

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> Order(string orderId, CancellationToken cancellationToken)
    {
        await sessions.Require(HttpContext, false, cancellationToken);
        return Ok(await mediator.SendQuery<ConsoleQuery, System.Text.Json.JsonElement>(new("order", orderId), cancellationToken));
    }

    [HttpPost("connect")]
    public Task<IActionResult> Connect(CancellationToken cancellationToken) => Command(new("connect"), cancellationToken);

    [HttpPost("publish")]
    public Task<IActionResult> Publish(CancellationToken cancellationToken) => Command(new("publish"), cancellationToken);

    [HttpPost("enable")]
    public Task<IActionResult> Enable(ConsoleEnableRequest request, CancellationToken cancellationToken)
        => Command(new("enable", Enable: request.Enable), cancellationToken);

    [HttpPost("orders/{orderId}/decision")]
    public Task<IActionResult> Decision(string orderId, ConsoleDecisionRequest request, CancellationToken cancellationToken)
        => Command(new("decision", OrderId: orderId, Decision: request), cancellationToken);

    private async Task<IActionResult> Command(ConsoleCommand command, CancellationToken cancellationToken)
    {
        var session = await sessions.Require(HttpContext, true, cancellationToken);
        return Ok(await mediator.SendCommand<ConsoleCommand, System.Text.Json.JsonElement>(command with { SessionHash = session }, cancellationToken));
    }
}
