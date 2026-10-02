using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface IUberOrderNormalizer
{
    TenantOrderRequest Normalize(JsonElement order, Guid expectedOrderId, TenantStoreBinding binding);
}
