using System.Collections.Concurrent;
using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class FakeUberSandboxClient : IUberSandboxClient
{
    public ConcurrentQueue<(HttpMethod Method, string Path, JsonElement? Body)> Calls { get; } = new();
    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Grants { get; } = new();
    public Guid MerchantStoreId { get; set; } = GatewayFixture.StoreId;
    public Guid OrderStoreId { get; set; } = GatewayFixture.StoreId;
    public JsonElement Menu { get; set; } = ProviderJson.Encode(new { menus = Array.Empty<object>() });
    public bool CorruptMenuReadback { get; set; }
    public bool ThrowOnDecision { get; set; }
    public int DecisionStatus { get; set; } = 204;
    public string OrderState { get; set; } = "CREATED";
    public Func<Task>? BeforeDecision { get; set; }

    public Task<ProviderReply> Token(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        Grants.Enqueue(new Dictionary<string, string>(fields));
        return Task.FromResult(new ProviderReply(200, ProviderJson.Encode(new
        {
            access_token = fields["grant_type"] == "authorization_code" ? "merchant-token-not-for-storage" : "app-token-not-for-logs",
            token_type = "Bearer",
            expires_in = 2_592_000,
            scope = fields.TryGetValue("scope", out var scope) ? scope : "eats.pos_provisioning",
        }), ""));
    }

    public async Task<ProviderReply> Send(HttpMethod method, string path, string token, JsonElement? body, CancellationToken cancellationToken)
    {
        Calls.Enqueue((method, path, body));
        var json = ProviderJson.Encode(new { });
        var status = 200;
        if (path.StartsWith("/v1/eats/stores?", StringComparison.Ordinal))
            json = ProviderJson.Encode(new { stores = new[] { new { store_id = MerchantStoreId } } });
        else if (path.EndsWith("/pos_data", StringComparison.Ordinal))
            json = ProviderJson.Encode(new
            {
                store_id = GatewayFixture.StoreId,
                integration_enabled = true,
                order_manager_client_id = GatewayFixture.ClientId,
                is_order_manager_pending = false,
                require_manual_acceptance = false
            });
        else if (path.EndsWith("/menus", StringComparison.Ordinal))
        {
            if (method == HttpMethod.Put) Menu = body!.Value;
            json = CorruptMenuReadback ? ProviderJson.Encode(new { menus = Array.Empty<object>() }) : Menu;
        }
        else if (path.StartsWith("/v2/eats/order/", StringComparison.Ordinal))
            json = ProviderJson.Encode(new
            {
                id = path.Split('/')[^1],
                current_state = OrderState,
                store = new { id = OrderStoreId },
                cart = new { items = new[] { new { title = "Fixture meal", special_instructions = "No peanuts; severe allergy", quantity = 1 } } },
                special_instructions = "Please read item notes"
            });
        else if (path.EndsWith("_pos_order", StringComparison.Ordinal))
        {
            if (BeforeDecision is not null) await BeforeDecision();
            if (ThrowOnDecision) throw new ChannelConsoleException(504, "Fixture ambiguous timeout");
            status = DecisionStatus;
            if (status == 204) OrderState = path.EndsWith("accept_pos_order", StringComparison.Ordinal) ? "ACCEPTED" : "DENIED";
        }
        else throw new ArgumentException("Unexpected provider path.", nameof(path));
        return new(status, json, "");
    }
}
