using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxOrders(IOptions<UberWebhookSettings> options, IUberSandboxClient provider,
    ISandboxTokens tokens, IConsoleRepository repository, ISandboxConnection connection, TimeProvider clock) : ISandboxOrders
{
    private string ClientId => options.Value.ClientId;
    private Guid StoreId => options.Value.StoreIds.Single();

    public async Task<JsonElement> Receipts(CancellationToken cancellationToken)
        => ProviderJson.Encode(await repository.RecentReceipts(ClientId, StoreId, cancellationToken));

    public async Task<JsonElement> Read(string orderId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(orderId, "D", out var id)
            || !await repository.HasOrder(ClientId, StoreId, id.ToString(), cancellationToken))
            throw new ChannelConsoleException(404, "No authenticated provider order evidence exists for this sandbox store.");
        var result = await provider.Send(HttpMethod.Get, $"/v2/eats/order/{id:D}", await tokens.AppToken(cancellationToken), null, cancellationToken);
        ProviderJson.RequireSuccess(result, "order retrieval");
        if (!Guid.TryParse(ProviderJson.Text(result.Body, "id"), out var returned) || returned != id
            || !result.Body.TryGetProperty("store", out var store)
            || !Guid.TryParse(ProviderJson.Text(store, "id"), out var storeId) || storeId != StoreId)
            throw new ChannelConsoleException(502, "Uber returned an order outside this sandbox store.");
        var enrolledAt = await repository.RecoveryEnrollment(ClientId, StoreId, id, cancellationToken);
        if (enrolledAt is not null && (!ProviderOrderTimestamp.TryRead(ProviderJson.Text(result.Body, "placed_at"),
            clock.GetUtcNow(), out var placedAt) || placedAt < enrolledAt))
            throw new ChannelConsoleException(502, "Uber's canonical order timestamp is invalid or predates its recovered enrollment boundary.");
        var recorded = await repository.FindAction(ClientId, StoreId, orderId, cancellationToken);
        var state = ProviderJson.Text(result.Body, "current_state");
        if (recorded is { State: "Pending" or "Unknown" }
            && (recorded.Action == "accept" && state == "ACCEPTED" || recorded.Action == "deny" && state == "DENIED"))
            await repository.FinishAction(ClientId, StoreId, orderId, "Succeeded", cancellationToken);
        return result.Body;
    }

    public async Task<JsonElement> Decide(string orderId, string action, string reason, CancellationToken cancellationToken)
    {
        if (action is not ("accept" or "deny") || string.IsNullOrWhiteSpace(reason) || reason.Length > 250
            || reason.Any(char.IsControl))
            throw new ChannelConsoleException(400, "Choose accept or deny and provide a reason of 1–250 characters.");
        var order = await Read(orderId, cancellationToken);
        if (ProviderJson.Text(order, "current_state") != "CREATED")
            throw new ChannelConsoleException(409, "Only a newly created order can be accepted or denied. Refresh its status.");
        var config = await connection.Configuration(cancellationToken);
        if (!ProviderJson.Flag(config, "enabled") || !ProviderJson.Flag(config, "orderManager") || ProviderJson.Flag(config, "pending"))
            throw new ChannelConsoleException(409, "Enable order testing and wait for Uber to confirm the order manager.");
        var token = await tokens.AppToken(cancellationToken);
        if (!await repository.ClaimAction(ClientId, StoreId, orderId, action, cancellationToken))
            throw new ChannelConsoleException(409, "A decision was already sent or is uncertain. Refresh the order; do not resend it.");
        var body = action == "accept" ? ProviderJson.Encode(new
        {
            reason,
            external_reference_id = $"sofra-sandbox-{orderId}",
            fields_relayed = new { order_special_instructions = true, item_special_instructions = true, item_special_requests = true, promotions = false },
        }) : ProviderJson.Encode(new { reason = new { explanation = reason, code = "OTHER" } });
        ProviderReply result;
        try
        {
            result = await provider.Send(HttpMethod.Post, $"/v1/eats/orders/{orderId}/{action}_pos_order", token, body, cancellationToken);
        }
        catch
        {
            await MarkUncertain(orderId);
            throw;
        }
        // Known replies are outside the transport catch: an old rejected attempt cannot touch a retry.
        var outcome = DecisionOutcome(result);
        await Finish(orderId, outcome);
        ProviderJson.RequireSuccess(result, "order decision");
        return ProviderJson.Encode(new { action, state = outcome, message = "Uber acknowledged the decision. Refresh for the current order state." });
    }

    private async Task MarkUncertain(string orderId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if ((await repository.FindAction(ClientId, StoreId, orderId, timeout.Token))?.State == "Pending")
            await repository.FinishAction(ClientId, StoreId, orderId, "Unknown", timeout.Token);
    }

    private static string DecisionOutcome(ProviderReply reply)
    {
        if (reply.IsSuccess) return "Succeeded";
        return reply.Status is >= 400 and < 500 ? "Failed" : "Unknown";
    }

    private async Task Finish(string orderId, string state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await repository.FinishAction(ClientId, StoreId, orderId, state, timeout.Token);
    }
}
