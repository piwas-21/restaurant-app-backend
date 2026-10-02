using System.Collections.Concurrent;
using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class FakeUberSandboxClient : IUberSandboxClient
{
    public ConcurrentQueue<(HttpMethod Method, string Path, JsonElement? Body)> Calls { get; } = new();
    public ConcurrentQueue<IReadOnlyDictionary<string, string>> Grants { get; } = new();
    public bool ManualAcceptance { get; set; }
    public Guid MerchantStoreId { get; set; } = GatewayFixture.StoreId;
    public Guid StoreDetailsStoreId { get; set; } = GatewayFixture.StoreId;
    public string StoreName { get; set; } = "Sofra Sandbox Kitchen";
    public int StoreDetailsStatus { get; set; } = 200;
    public Guid OrderStoreId { get; set; } = GatewayFixture.StoreId;
    public JsonElement Menu { get; set; } = ProviderJson.Encode(new { menus = Array.Empty<object>() });
    public JsonElement CreatedOrders { get; set; } = ProviderJson.Encode(new { orders = Array.Empty<object>() });
    public string CreatedOrdersClientId { get; set; } = string.Empty;
    public int CreatedOrdersStatus { get; set; } = 200;
    public int StockUpdateStatus { get; set; } = 204;
    public bool IgnoreStockUpdate { get; set; }
    public bool ThrowAfterStockUpdate { get; set; }
    public string MenuClientId { get; set; } = string.Empty;
    public bool CorruptMenuReadback { get; set; }
    public bool ThrowAfterMenuUpload { get; set; }
    public Func<Task>? BeforeMenuUpload { get; set; }
    public Func<Task>? BeforeConnectionConfiguration { get; set; }
    public bool ThrowOnDecision { get; set; }
    public int DecisionStatus { get; set; } = 204;
    public JsonElement? CanonicalOrder { get; set; }
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
        else if (path.Contains("/created-orders?", StringComparison.Ordinal))
            return new(CreatedOrdersStatus, CreatedOrders, CreatedOrdersClientId);
        else if (path.EndsWith("/pos_data", StringComparison.Ordinal))
        {
            if (BeforeConnectionConfiguration is not null) await BeforeConnectionConfiguration();
            json = ProviderJson.Encode(new
            {
                store_id = GatewayFixture.StoreId,
                integration_enabled = true,
                order_manager_client_id = GatewayFixture.ClientId,
                is_order_manager_pending = false,
                require_manual_acceptance = ManualAcceptance
            });
        }
        else if (path == $"/v1/eats/stores/{GatewayFixture.StoreId:D}")
            return new(StoreDetailsStatus, ProviderJson.Encode(new { store_id = StoreDetailsStoreId, name = StoreName }), "");
        else if (path.Contains("/menus/items/", StringComparison.Ordinal))
        {
            if (StockUpdateStatus == 204 && !IgnoreStockUpdate)
            {
                var menu = System.Text.Json.Nodes.JsonNode.Parse(Menu.GetRawText())!.AsObject();
                var itemId = Uri.UnescapeDataString(path.Split('/')[^1]);
                var item = menu["items"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == itemId)!;
                item["suspension_info"] = System.Text.Json.Nodes.JsonNode.Parse(body!.Value.GetProperty("suspension_info").GetRawText());
                Menu = JsonSerializer.SerializeToElement(menu);
            }
            if (ThrowAfterStockUpdate) throw new ChannelConsoleException(504, "Private fixture timeout; token must never be logged");
            return new(StockUpdateStatus, json, "");
        }
        else if (path.EndsWith("/menus", StringComparison.Ordinal))
        {
            if (method == HttpMethod.Put)
            {
                if (BeforeMenuUpload is not null) await BeforeMenuUpload();
                Menu = body!.Value;
                if (ThrowAfterMenuUpload) throw new ChannelConsoleException(504, "Fixture lost response after menu upload.");
            }
            json = CorruptMenuReadback ? ProviderJson.Encode(new { menus = Array.Empty<object>() }) : Menu;
            return new(status, json, MenuClientId);
        }
        else if (path.StartsWith("/v2/eats/order/", StringComparison.Ordinal))
            json = CanonicalOrder ?? ProviderJson.Encode(new
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
