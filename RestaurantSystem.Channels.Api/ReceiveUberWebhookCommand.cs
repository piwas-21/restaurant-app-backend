using Microsoft.Extensions.Options;
using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed record ReceiveUberWebhookCommand(byte[] Body, string Signature, string Environment) : IChannelCommand<int>;

public sealed class ReceiveUberWebhookCommandHandler(
    IOptions<UberWebhookSettings> options,
    IWebhookInbox inbox,
    TimeProvider timeProvider,
    ILogger<ReceiveUberWebhookCommandHandler> logger) : IChannelCommandHandler<ReceiveUberWebhookCommand, int>
{
    public async Task<int> Handle(ReceiveUberWebhookCommand command, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsConfigured)
            return StatusCodes.Status503ServiceUnavailable;
        if (!UberSignature.Verify(command.Body, command.Signature, settings.ClientSecret))
            return StatusCodes.Status401Unauthorized;
        if (!string.Equals(command.Environment, "sandbox", StringComparison.Ordinal))
            return StatusCodes.Status403Forbidden;
        var receipt = UberNotification.Parse(command.Body, settings.ClientId, timeProvider.GetUtcNow());
        if (receipt is null)
            return StatusCodes.Status400BadRequest;
        if (!settings.StoreIds.Contains(receipt.StoreId))
            return StatusCodes.Status403Forbidden;
        try
        {
            var result = await inbox.Receive(receipt, cancellationToken);
            return result == InboxWriteResult.Conflict
                ? StatusCodes.Status409Conflict : StatusCodes.Status200OK;
        }
        catch (NpgsqlException)
        {
            // Do not log exception text, request payloads, store/customer IDs or connection strings.
            logger.LogError("Channel webhook persistence failed; event was not acknowledged.");
            return StatusCodes.Status503ServiceUnavailable;
        }
        catch (TimeoutException)
        {
            logger.LogError("Channel webhook persistence timed out; event was not acknowledged.");
            return StatusCodes.Status503ServiceUnavailable;
        }
    }
}
