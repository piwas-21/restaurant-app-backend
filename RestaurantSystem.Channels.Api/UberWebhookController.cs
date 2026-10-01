using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

[ApiController]
[Route("api/webhooks/uber-eats")]
public sealed class UberWebhookController(
    ChannelMediator mediator,
    IOptions<UberWebhookSettings> options) : ControllerBase
{
    // Provider-authenticated, not browser/session-authenticated. The command verifies the HMAC
    // over the original bytes before parsing and binds the event to an approved sandbox store.
    [HttpPost]
    [EnableRateLimiting("uber-webhook")]
    public async Task<IActionResult> Receive(
        [FromHeader(Name = "X-Uber-Signature")] string? signature,
        [FromHeader(Name = "X-Environment")] string? environment,
        CancellationToken cancellationToken)
    {
        if (Request.ContentLength > options.Value.MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + count > options.Value.MaxBodyBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        var command = new ReceiveUberWebhookCommand(body.ToArray(), signature ?? string.Empty, environment ?? string.Empty);
        return StatusCode(await mediator.SendCommand<ReceiveUberWebhookCommand, int>(command, cancellationToken));
    }
}
