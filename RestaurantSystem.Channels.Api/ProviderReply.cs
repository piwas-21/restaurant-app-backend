using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed record ProviderReply(int Status, JsonElement Body, string ClientId)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}
