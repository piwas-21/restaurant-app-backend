using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed record ConsoleCommand(string Operation, string SessionHash = "", string OrderId = "",
    bool Enable = false, ConsoleDecisionRequest? Decision = null, string PublicationRevision = "") : IChannelCommand<JsonElement>;

public sealed class ConsoleCommandHandler(ISandboxConnection connection, ISandboxMenu menu, ISandboxOrders orders)
    : IChannelCommandHandler<ConsoleCommand, JsonElement>
{
    public async Task<JsonElement> Handle(ConsoleCommand command, CancellationToken cancellationToken)
        => command.Operation switch
        {
            "connect" => ProviderJson.Encode(new { url = await connection.Start(command.SessionHash, cancellationToken) }),
            "publish" => await menu.Publish(command.PublicationRevision, cancellationToken),
            "enable" when command.Enable => ProviderJson.Encode(new { url = await connection.Start(command.SessionHash, cancellationToken, true) }),
            "enable" => await connection.EnableOrders(false, cancellationToken),
            "decision" when command.Decision is { ReviewedInstructions: true } decision
                => await orders.Decide(command.OrderId, decision.Action, decision.Reason, cancellationToken),
            _ => throw new ChannelConsoleException(400, "Review the complete order and customer instructions before deciding."),
        };
}
