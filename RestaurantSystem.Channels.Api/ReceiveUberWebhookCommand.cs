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
            return Reject(StatusCodes.Status503ServiceUnavailable, "missing-configuration");
        if (!UberSignature.Verify(command.Body, command.Signature, settings.ClientSecret))
            return Reject(StatusCodes.Status401Unauthorized, "invalid-signature");
        // Uber documents the signature header, but does not promise an environment header.
        // The testing client's HMAC key and approved store establish the sandbox boundary.
        if (command.Environment.Length > 0 && !string.Equals(command.Environment, "sandbox", StringComparison.Ordinal))
            return Reject(StatusCodes.Status403Forbidden, "unexpected-environment");
        var receipt = UberNotification.Parse(command.Body, settings.ClientId, timeProvider.GetUtcNow());
        if (receipt is null)
            return Reject(StatusCodes.Status400BadRequest, "invalid-envelope");
        if (!settings.StoreIds.Contains(receipt.StoreId))
            return Reject(StatusCodes.Status403Forbidden, "unapproved-store");
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

    private int Reject(int status, string reason)
    {
        // Only code-owned categories: never headers, bodies, provider URLs or identifiers.
        logger.LogWarning("Channel webhook rejected: {Reason} ({Status}).", reason, status);
        return status;
    }
}
