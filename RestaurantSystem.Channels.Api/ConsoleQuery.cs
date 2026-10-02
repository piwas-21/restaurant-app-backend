using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed record ConsoleQuery(string Operation, string OrderId = "") : IChannelQuery<JsonElement>;

public sealed class ConsoleQueryHandler(ISandboxConnection connection, ISandboxMenu menu, ISandboxOrders orders,
    IChannelAvailabilityStatus availability)
    : IChannelQueryHandler<ConsoleQuery, JsonElement>
{
    public async Task<JsonElement> Handle(ConsoleQuery query, CancellationToken cancellationToken)
        => query.Operation switch
        {
            "availability" => await availability.Read(cancellationToken),
            "configuration" => await connection.Configuration(cancellationToken),
            "preview" => menu.Preview(),
            "menu" => await menu.Read(cancellationToken),
            "verification" => await VerifyMenu(cancellationToken),
            "receipts" => await orders.Receipts(cancellationToken),
            "order" => await orders.Read(query.OrderId, cancellationToken),
            _ => throw new ChannelConsoleException(404, "The requested sandbox operation does not exist."),
        };

    private async Task<JsonElement> VerifyMenu(CancellationToken cancellationToken)
    {
        await menu.RequireVerified(cancellationToken);
        return ProviderJson.Encode(new { verified = true, revision = "sandbox-menu-v1" });
    }
}
