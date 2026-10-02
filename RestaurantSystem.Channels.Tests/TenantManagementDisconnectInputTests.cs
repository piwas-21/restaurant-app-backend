using System.Text.Json;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class TenantManagementDisconnectInputTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MissingStoreCannotBecomeAnImplicitZeroStore()
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TenantManagementDisconnectRequest>("{}", Options));

    [Fact]
    public void ExplicitStoreIsPreservedByTheGatewayRequestContract()
    {
        const string StoreId = "11111111-1111-1111-1111-111111111111";
        var request = JsonSerializer.Deserialize<TenantManagementDisconnectRequest>($"{{\"storeId\":\"{StoreId}\"}}", Options);
        Assert.Equal(Guid.Parse(StoreId), request!.StoreId);
    }
}
