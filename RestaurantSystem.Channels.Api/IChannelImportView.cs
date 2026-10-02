using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface IChannelImportView
{
    Task<JsonElement> Read(string cursor, CancellationToken cancellationToken);
}
